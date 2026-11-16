"""Export the current Blender scene to FBX using an explicit, versioned profile.

Run with:
  blender --background source.blend --python export_avatar_fbx.py -- --out avatar.fbx
  blender --background source.blend --python export_avatar_fbx.py -- --out avatar.fbx --roundtrip --json-out report.json
  blender --background source.blend --python export_avatar_fbx.py -- --dry-run
  blender --background --factory-startup --python export_avatar_fbx.py -- --self-test

What it guarantees:
  * Every FBX exporter option comes from the profile JSON. If Blender adds, removes or
    renames an option, the tool stops instead of falling back to a default.
  * The source scene is not changed: object transforms, mesh vertices, selection and the
    active object are snapshotted before the export and compared afterwards. The .blend
    file is never saved.
  * The written file is read back and its recorded axis/unit header is compared with the
    profile's expectation.
  * --roundtrip re-imports the FBX into an empty Blender session and compares world-space
    geometry, bones, shape keys, vertex groups and materials against the source.

What it does not guarantee: a Blender round trip is not a Unity import. The Unity side of
the validation is a manual plan in docs/FBX_EXPORT_PROFILE.md until a VRChat project exists.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import tempfile
import traceback
from pathlib import Path

import bpy
from mathutils import Vector

DEFAULT_PROFILE = Path(__file__).resolve().parent / "profiles" / "unity_avatar_fbx_v1.json"

# Operator options that are file-dialog plumbing rather than export decisions.
EXPORT_EXEMPT = {"filepath", "check_existing", "filter_glob"}
IMPORT_EXEMPT = {"filepath", "directory", "filter_glob", "files", "ui_tab"}


class ExportError(Exception):
    """A condition that stops the export. The message is meant for the user."""


# --------------------------------------------------------------------------- profile

def _operator_options(operator, exempt: set) -> dict:
    return {p.identifier: p for p in operator.get_rna_type().properties
            if p.identifier != "rna_type" and p.identifier not in exempt}


def _check_coverage(section: str, given: dict, operator, exempt: set) -> None:
    options = _operator_options(operator, exempt)
    missing = sorted(set(options) - set(given))
    unknown = sorted(set(given) - set(options))
    if missing or unknown:
        raise ExportError(
            f"Profile section '{section}' does not match this Blender's FBX options. "
            f"Not stated in profile: {missing or 'none'}. Not known to Blender: {unknown or 'none'}. "
            "Decide each option explicitly, update the profile, and re-run the self-test.")
    for name, value in given.items():
        prop = options[name]
        if prop.type == "ENUM":
            allowed = {item.identifier for item in prop.enum_items}
            values = set(value) if isinstance(value, list) else {value}
            if not values <= allowed:
                raise ExportError(f"Profile '{section}.{name}' = {value!r} is not one of {sorted(allowed)}.")


def load_profile(path: Path) -> dict:
    profile = json.loads(Path(path).read_text(encoding="utf-8"))
    for key in ("profile", "profile_version", "export_scene_fbx", "expected_fbx_global_settings",
                "roundtrip_import_scene_fbx", "roundtrip_tolerance_m"):
        if key not in profile:
            raise ExportError(f"Profile {path} has no '{key}' entry.")
    _check_coverage("export_scene_fbx", profile["export_scene_fbx"], bpy.ops.export_scene.fbx, EXPORT_EXEMPT)
    _check_coverage("roundtrip_import_scene_fbx", profile["roundtrip_import_scene_fbx"],
                    bpy.ops.import_scene.fbx, IMPORT_EXEMPT)
    return profile


def _operator_kwargs(settings: dict) -> dict:
    # JSON lists are Blender flag enums (sets), e.g. object_types.
    return {k: (set(v) if isinstance(v, list) else v) for k, v in settings.items()}


# --------------------------------------------------------------- source scene handling

def _round_list(values, digits: int = 6) -> list:
    return [round(float(v), digits) for v in values]


def snapshot_scene() -> dict:
    """Everything the export must leave untouched, in a comparable form."""
    bpy.context.view_layer.update()
    objects = {}
    for obj in bpy.context.scene.objects:
        row = {
            "matrix_world": [_round_list(r, 9) for r in obj.matrix_world],
            "location": _round_list(obj.location, 9),
            "rotation_euler": _round_list(obj.rotation_euler, 9),
            "scale": _round_list(obj.scale, 9),
            "selected": obj.select_get(),
            "hidden": obj.hide_get(),
        }
        if obj.type == "MESH":
            coords = [0.0] * (len(obj.data.vertices) * 3)
            obj.data.vertices.foreach_get("co", coords)
            row["vertex_hash"] = hashlib.sha256(repr(coords).encode()).hexdigest()
            row["modifiers"] = [(m.name, m.type) for m in obj.modifiers]
        objects[obj.name] = row
    active = bpy.context.view_layer.objects.active
    return {"objects": objects, "active": active.name if active else None,
            "unit_scale_length": float(bpy.context.scene.unit_settings.scale_length)}


def preflight(profile: dict) -> list:
    """Report conditions that affect the export. Nothing is fixed here."""
    issues = []
    scene = bpy.context.scene
    types = set(profile["export_scene_fbx"]["object_types"])
    exported = [o for o in scene.objects if o.type in types]
    if bpy.context.mode != "OBJECT":
        issues.append({"severity": "error", "code": "not_object_mode",
                       "message": f"Blender is in {bpy.context.mode} mode; save the file in Object Mode and retry."})
    if not exported:
        issues.append({"severity": "error", "code": "nothing_to_export",
                       "message": f"Scene has no objects of the profile's types {sorted(types)}."})
    scale_length = float(scene.unit_settings.scale_length)
    if abs(scale_length - 1.0) > 1e-6:
        issues.append({"severity": "warning", "code": "scene_unit_scale",
                       "message": f"Scene unit scale is {scale_length}, not 1.0. The exporter multiplies it into "
                                  "the FBX unit scale, so the header check below will report a different unit."})
    for obj in exported:
        if any(abs(s - 1.0) > 1e-4 for s in obj.scale):
            issues.append({"severity": "warning", "code": "unapplied_object_scale", "object": obj.name,
                           "message": f"Object scale is {_round_list(obj.scale, 4)}. It is exported as-is, not applied."})
        if any(abs(r) > 1e-4 for r in obj.rotation_euler):
            degrees = [round(math.degrees(r), 3) for r in obj.rotation_euler]
            issues.append({"severity": "warning", "code": "unapplied_object_rotation", "object": obj.name,
                           "message": f"Object rotation is {degrees} degrees. It is exported as-is, not applied."})
        if obj.type == "MESH" and obj.data.shape_keys:
            keys = obj.data.shape_keys
            active = {k.name: round(float(k.value), 4) for k in keys.key_blocks
                      if k != keys.reference_key and abs(k.value) > 1e-6}
            if active:
                issues.append({"severity": "warning", "code": "shape_key_value_nonzero", "object": obj.name,
                               "message": f"Shape keys with a non-zero value: {active}. The value is exported as the "
                                          "blend shape's default weight, so Unity shows the mesh with these shapes "
                                          "already applied (measured in Unity 2022.3.22f1). Set them to 0 on a copy "
                                          "of the file unless that is intended."})
        if obj.type == "MESH" and not profile["export_scene_fbx"]["use_mesh_modifiers"]:
            skipped = [m.name for m in obj.modifiers if m.type != "ARMATURE"]
            if skipped:
                issues.append({"severity": "warning", "code": "modifiers_not_applied", "object": obj.name,
                               "message": f"Modifiers {skipped} are not applied by this profile; the FBX holds "
                                          "the unmodified mesh. Apply them on a copy of the file if needed."})
    return issues


def capture_metrics() -> dict:
    """World-space facts about the scene, used as the round-trip reference."""
    bpy.context.view_layer.update()
    meshes, armatures = {}, {}
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            mesh = obj.data
            mesh.calc_loop_triangles()
            world = [obj.matrix_world @ v.co for v in mesh.vertices]
            keys = {}
            if mesh.shape_keys:
                basis = mesh.shape_keys.reference_key
                rotation = obj.matrix_world.to_3x3()
                for block in mesh.shape_keys.key_blocks:
                    if block == basis:
                        continue
                    keys[block.name] = [list(rotation @ (block.data[i].co - basis.data[i].co))
                                        for i in range(len(mesh.vertices))]
            meshes[obj.name] = {
                "vertices_world": [list(v) for v in world],
                "triangles": len(mesh.loop_triangles),
                "bounds_min": [min(v[i] for v in world) for i in range(3)] if world else None,
                "bounds_max": [max(v[i] for v in world) for i in range(3)] if world else None,
                "shape_key_deltas_world": keys,
                "vertex_groups": sorted(g.name for g in obj.vertex_groups),
                "materials": [s.material.name if s.material else None for s in obj.material_slots],
                "object_scale": _round_list(obj.scale),
                "object_rotation_deg": [round(math.degrees(r), 4) for r in obj.rotation_euler],
            }
        elif obj.type == "ARMATURE":
            armatures[obj.name] = {
                "bones": {b.name: {"parent": b.parent.name if b.parent else None,
                                   "head_world": list(obj.matrix_world @ b.head_local),
                                   "tail_world": list(obj.matrix_world @ b.tail_local)}
                          for b in obj.data.bones},
                "object_scale": _round_list(obj.scale),
                "object_rotation_deg": [round(math.degrees(r), 4) for r in obj.rotation_euler],
            }
    return {"meshes": meshes, "armatures": armatures}


# ------------------------------------------------------------------ FBX file inspection

def read_fbx_facts(path: Path) -> dict:
    """Read the axis/unit header and per-object local transforms stored in the file.

    These are the numbers another application (Unity) reads, independent of how Blender's
    importer chooses to interpret them.
    """
    from io_scene_fbx import parse_fbx

    root, version = parse_fbx.parse(str(path))

    def child(elem, name):
        return next((e for e in elem.elems if e.id == name), None)

    def properties(elem) -> dict:
        block = child(elem, b"Properties70")
        if block is None:
            return {}
        result = {}
        for p in block.elems:
            values = list(p.props[4:])
            result[p.props[0].decode()] = values[0] if len(values) == 1 else values
        return result

    settings = properties(child(root, b"GlobalSettings"))
    wanted = ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign",
              "UnitScaleFactor", "OriginalUnitScaleFactor")
    models, blend_shapes = {}, {}
    for elem in child(root, b"Objects").elems:
        if elem.id == b"Deformer" and elem.props[2] == b"BlendShapeChannel":
            percent = child(elem, b"DeformPercent")
            blend_shapes[elem.props[1].split(b"\x00")[0].decode()] = float(percent.props[0]) if percent else None
        if elem.id != b"Model":
            continue
        name = elem.props[1].split(b"\x00")[0].decode()
        props = properties(elem)
        models[name] = {
            "class": elem.props[2].decode(),
            "lcl_translation": _round_list(props.get("Lcl Translation", [0, 0, 0]), 5),
            "lcl_rotation_deg": _round_list(props.get("Lcl Rotation", [0, 0, 0]), 4),
            "lcl_scaling": _round_list(props.get("Lcl Scaling", [1, 1, 1]), 5),
            "pre_rotation_deg": _round_list(props["PreRotation"], 4) if "PreRotation" in props else None,
        }
    return {"fbx_version": version,
            "global_settings": {k: settings[k] for k in wanted if k in settings},
            "models": models,
            "blend_shape_default_percent": blend_shapes}


NATIVE_FILE_PROPERTY = b"Original|ApplicationNativeFile"


def strip_source_path(path: Path):
    """Blank the folder part of the source .blend path that Blender writes into every FBX.

    Blender stores the full path of the open .blend, including the user's home folder, in the FBX
    header property Original|ApplicationNativeFile, which reveals the user's folders when the FBX
    is shared. The value is overwritten in place with underscores followed by the file name,
    keeping its length, so no other byte of the file moves. Returns the number of characters blanked
    (0 when there was no folder to blank, None when the property was not found).
    """
    data = bytearray(Path(path).read_bytes())
    at = data.find(NATIVE_FILE_PROPERTY)
    if at < 0:
        return None
    # Property record: name, type "KString", flags, empty label, then the value as 'S' + uint32 length + bytes.
    cursor = at + len(NATIVE_FILE_PROPERTY)
    layout_error = ExportError("Blender wrote the FBX's source-file property in an unexpected layout, so the folder "
                               f"path inside it could not be removed. Do not share this FBX: {path}")
    for _ in range(3):                                   # skip the type, flags and label strings
        if data[cursor:cursor + 1] != b"S":
            raise layout_error
        cursor += 5 + int.from_bytes(data[cursor + 1:cursor + 5], "little")
    if data[cursor:cursor + 1] != b"S":
        raise layout_error
    length = int.from_bytes(data[cursor + 1:cursor + 5], "little")
    start = cursor + 5
    value = bytes(data[start:start + length])
    name = value.replace(b"\\", b"/").rsplit(b"/", 1)[-1]
    if len(name) == len(value):
        return 0
    data[start:start + length] = b"_" * (length - len(name)) + name
    Path(path).write_bytes(bytes(data))
    return length - len(name)


def check_fbx_header(facts: dict, expected: dict) -> list:
    problems = []
    for key, want in expected.items():
        got = facts["global_settings"].get(key)
        if got is None or abs(float(got) - float(want)) > 1e-6:
            problems.append(f"FBX header {key} is {got!r}, profile expects {want!r}")
    return problems


# ------------------------------------------------------------------------------ export

def export_fbx(profile: dict, out_path: Path, overwrite: bool = False) -> dict:
    out_path = Path(out_path).resolve()
    if out_path.suffix.lower() != ".fbx":
        raise ExportError(f"Output must be a .fbx file, got: {out_path}")
    if out_path.exists() and not overwrite:
        raise ExportError(f"Output already exists (pass --overwrite to replace it): {out_path}")

    issues = preflight(profile)
    errors = [i for i in issues if i["severity"] == "error"]
    if errors:
        raise ExportError("; ".join(i["message"] for i in errors))

    before = snapshot_scene()
    out_path.parent.mkdir(parents=True, exist_ok=True)
    result = bpy.ops.export_scene.fbx(filepath=str(out_path), check_existing=False,
                                      **_operator_kwargs(profile["export_scene_fbx"]))
    if "FINISHED" not in result or not out_path.is_file():
        raise ExportError(f"Blender's FBX exporter did not finish: {result}")
    after = snapshot_scene()
    if before != after:
        changed = sorted(n for n in before["objects"] if before["objects"][n] != after["objects"].get(n))
        raise ExportError(f"Source scene changed during export (objects: {changed or 'scene-level state'}). "
                          "Do not use this FBX; report the Blender version and profile.")

    stripped = strip_source_path(out_path) if profile.get("strip_source_path", False) else None
    facts = read_fbx_facts(out_path)
    return {
        "output": str(out_path),
        "output_bytes": out_path.stat().st_size,
        "issues": issues,
        "source_unchanged": True,
        "source_path_stripped": stripped,
        "fbx": facts,
        "header_problems": check_fbx_header(facts, profile["expected_fbx_global_settings"]),
    }


# -------------------------------------------------------------------------- round trip

def _max_error(a: list, b: list) -> float:
    return max(((Vector(x) - Vector(y)).length for x, y in zip(a, b)), default=0.0)


def roundtrip_check(profile: dict, fbx_path: Path, expected: dict) -> dict:
    """Re-import the FBX into an empty session and compare it with the source metrics.

    This discards the in-memory session (nothing is saved), so it must be the last step.
    """
    tolerance = float(profile["roundtrip_tolerance_m"])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = bpy.ops.import_scene.fbx(filepath=str(fbx_path),
                                      **_operator_kwargs(profile["roundtrip_import_scene_fbx"]))
    if "FINISHED" not in result:
        return {"status": "fail", "failures": [f"Blender's FBX importer did not finish: {result}"], "measured": {}}
    got = capture_metrics()
    failures, measured = [], {"meshes": {}, "armatures": {}}

    for name, want in expected["meshes"].items():
        have = got["meshes"].get(name)
        if have is None:
            failures.append(f"mesh '{name}' missing after import (found {sorted(got['meshes'])})")
            continue
        row = {"imported_object_scale": have["object_scale"],
               "imported_object_rotation_deg": have["object_rotation_deg"],
               "bounds_min": _round_list(have["bounds_min"], 5), "bounds_max": _round_list(have["bounds_max"], 5)}
        if len(have["vertices_world"]) != len(want["vertices_world"]):
            failures.append(f"mesh '{name}': vertex count {len(have['vertices_world'])} != {len(want['vertices_world'])}")
        else:
            row["max_vertex_error_m"] = _max_error(have["vertices_world"], want["vertices_world"])
            if row["max_vertex_error_m"] > tolerance:
                failures.append(f"mesh '{name}': world-space vertices moved by up to {row['max_vertex_error_m']:.6f} m")
        if have["triangles"] != want["triangles"]:
            failures.append(f"mesh '{name}': triangle count {have['triangles']} != {want['triangles']}")
        if sorted(have["shape_key_deltas_world"]) != sorted(want["shape_key_deltas_world"]):
            failures.append(f"mesh '{name}': shape keys {sorted(have['shape_key_deltas_world'])} != "
                            f"{sorted(want['shape_key_deltas_world'])}")
        else:
            errors = [_max_error(have["shape_key_deltas_world"][k], want["shape_key_deltas_world"][k])
                      for k in want["shape_key_deltas_world"]]
            row["max_shape_key_error_m"] = max(errors, default=0.0)
            if row["max_shape_key_error_m"] > tolerance:
                failures.append(f"mesh '{name}': shape key offsets differ by up to {row['max_shape_key_error_m']:.6f} m")
        for field in ("vertex_groups", "materials"):
            if have[field] != want[field]:
                failures.append(f"mesh '{name}': {field} {have[field]} != {want[field]}")
        measured["meshes"][name] = row

    for name, want in expected["armatures"].items():
        have = got["armatures"].get(name)
        if have is None:
            failures.append(f"armature '{name}' missing after import (found {sorted(got['armatures'])})")
            continue
        row = {"imported_object_scale": have["object_scale"],
               "imported_object_rotation_deg": have["object_rotation_deg"]}
        if sorted(have["bones"]) != sorted(want["bones"]):
            failures.append(f"armature '{name}': bones {sorted(have['bones'])} != {sorted(want['bones'])}")
        else:
            names = sorted(want["bones"])
            row["max_bone_head_error_m"] = _max_error([have["bones"][b]["head_world"] for b in names],
                                                      [want["bones"][b]["head_world"] for b in names])
            # FBX stores bone joints, not Blender tails; tail drift is reported but not a failure.
            row["max_bone_tail_error_m"] = _max_error([have["bones"][b]["tail_world"] for b in names],
                                                      [want["bones"][b]["tail_world"] for b in names])
            if row["max_bone_head_error_m"] > tolerance:
                failures.append(f"armature '{name}': bone heads moved by up to {row['max_bone_head_error_m']:.6f} m")
            wrong_parents = [b for b in names if have["bones"][b]["parent"] != want["bones"][b]["parent"]]
            if wrong_parents:
                failures.append(f"armature '{name}': parent changed for bones {wrong_parents}")
        measured["armatures"][name] = row

    return {"status": "fail" if failures else "pass", "tolerance_m": tolerance,
            "failures": failures, "measured": measured}


# ---------------------------------------------------------------------------- fixture

def build_fixture() -> None:
    """A small skinned test avatar that is different along every axis.

    Blender convention: Z up, character faces -Y, character's left is +X.
    Body 0.4 x 0.2 x 1.6 m standing on the origin, a 'nose' vertex towards -Y and a
    'left hand' vertex towards +X, so a flipped or swapped axis cannot round-trip cleanly.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    collection = bpy.context.scene.collection

    arm_data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    layout = [("Hips", None, (0, 0, 0.9), (0, 0, 1.1)),
              ("Spine", "Hips", (0, 0, 1.1), (0, 0, 1.4)),
              ("Head", "Spine", (0, 0, 1.4), (0, 0, 1.6)),
              ("LeftArm", "Spine", (0.2, 0, 1.3), (0.5, 0, 1.3))]
    for name, parent, head, tail in layout:
        bone = arm_data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")

    x, y, z = 0.2, 0.1, 1.6
    vertices = [(-x, -y, 0), (x, -y, 0), (x, y, 0), (-x, y, 0),
                (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z),
                (0.0, -0.3, 1.5),   # 8: nose, towards the character's front (-Y)
                (0.6, 0.0, 1.3)]    # 9: left hand, towards the character's left (+X)
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
             (4, 8, 5), (5, 9, 6)]
    mesh = bpy.data.meshes.new("Body")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    body = bpy.data.objects.new("Body", mesh)
    collection.objects.link(body)
    mesh.materials.append(bpy.data.materials.new("FixtureSkin"))

    weights = {"Hips": [0, 1, 2, 3], "Spine": [4, 5, 6, 7], "Head": [8], "LeftArm": [9]}
    for bone_name, indices in weights.items():
        body.vertex_groups.new(name=bone_name).add(indices, 1.0, "REPLACE")
    body.parent = arm
    body.modifiers.new("Armature", "ARMATURE").object = arm

    body.shape_key_add(name="Basis")
    key = body.shape_key_add(name="vrc.v_aa")
    key.data[8].co = Vector(vertices[8]) + Vector((0.0, -0.05, -0.02))
    # Blender creates keys through the API with value 1.0, and the exporter writes the value as the
    # blend shape's default weight. Keep the fixture at rest so it imports with the shape off.
    key.value = 0.0
    bpy.context.view_layer.update()


# Full humanoid test rig: (bone, parent, head, tail) for the centre line and the left side.
# Blender convention: Z up, facing -Y, the character's left is +X. Bones use Unity's slot names.
_HUMANOID_CENTRE = [
    ("Hips", None, (0, 0, 0.95), (0, 0, 1.05)),
    ("Spine", "Hips", (0, 0, 1.05), (0, 0, 1.20)),
    ("Chest", "Spine", (0, 0, 1.20), (0, 0, 1.40)),
    ("Neck", "Chest", (0, 0, 1.40), (0, 0, 1.50)),
    ("Head", "Neck", (0, 0, 1.50), (0, 0, 1.70)),
]
_HUMANOID_LEFT = [
    ("Shoulder", "Chest", (0.03, 0, 1.38), (0.15, 0, 1.38)),
    ("UpperArm", "LeftShoulder", (0.15, 0, 1.38), (0.42, 0, 1.38)),
    ("LowerArm", "LeftUpperArm", (0.42, 0, 1.38), (0.67, 0, 1.38)),
    ("Hand", "LeftLowerArm", (0.67, 0, 1.38), (0.77, 0, 1.38)),
    ("UpperLeg", "Hips", (0.09, 0, 0.95), (0.09, 0, 0.50)),
    ("LowerLeg", "LeftUpperLeg", (0.09, 0, 0.50), (0.09, 0, 0.10)),
    ("Foot", "LeftLowerLeg", (0.09, 0, 0.10), (0.09, -0.12, 0.02)),
    ("Toes", "LeftFoot", (0.09, -0.12, 0.02), (0.09, -0.20, 0.02)),
]


# Three fingers per hand (thumb, index, middle), three segments each, for the finger variants.
_FINGERS = (("Thumb", (0.0, -0.03, -0.01), (0.02, -0.02, 0.0)), ("Index", (0.0, 0.0, 0.012), (0.025, 0.0, 0.0)),
            ("Middle", (0.0, 0.0, -0.012), (0.025, 0.0, 0.0)))
_SEGMENTS = ("Proximal", "Intermediate", "Distal")

# Bone names in other common conventions, keyed by Unity slot name ("{s}" is L/R, "{Side}" Left/Right).
_NAMINGS = {
    "unity": {},
    "blender": {"Hips": "hips", "Spine": "spine", "Chest": "chest", "Neck": "neck", "Head": "head",
                "Shoulder": "shoulder.{s}", "UpperArm": "upper_arm.{s}", "LowerArm": "forearm.{s}", "Hand": "hand.{s}",
                "UpperLeg": "thigh.{s}", "LowerLeg": "shin.{s}", "Foot": "foot.{s}", "Toes": "toe.{s}",
                "ThumbProximal": "thumb.01.{s}", "ThumbIntermediate": "thumb.02.{s}", "ThumbDistal": "thumb.03.{s}",
                "IndexProximal": "index.01.{s}", "IndexIntermediate": "index.02.{s}", "IndexDistal": "index.03.{s}",
                "MiddleProximal": "middle.01.{s}", "MiddleIntermediate": "middle.02.{s}", "MiddleDistal": "middle.03.{s}"},
    "mixamo": {"Hips": "mixamorig:Hips", "Spine": "mixamorig:Spine", "Chest": "mixamorig:Spine1",
               "Neck": "mixamorig:Neck", "Head": "mixamorig:Head", "Shoulder": "mixamorig:{Side}Shoulder",
               "UpperArm": "mixamorig:{Side}Arm", "LowerArm": "mixamorig:{Side}ForeArm", "Hand": "mixamorig:{Side}Hand",
               "UpperLeg": "mixamorig:{Side}UpLeg", "LowerLeg": "mixamorig:{Side}Leg", "Foot": "mixamorig:{Side}Foot",
               "Toes": "mixamorig:{Side}ToeBase",
               "ThumbProximal": "mixamorig:{Side}HandThumb1", "ThumbIntermediate": "mixamorig:{Side}HandThumb2",
               "ThumbDistal": "mixamorig:{Side}HandThumb3", "IndexProximal": "mixamorig:{Side}HandIndex1",
               "IndexIntermediate": "mixamorig:{Side}HandIndex2", "IndexDistal": "mixamorig:{Side}HandIndex3",
               "MiddleProximal": "mixamorig:{Side}HandMiddle1", "MiddleIntermediate": "mixamorig:{Side}HandMiddle2",
               "MiddleDistal": "mixamorig:{Side}HandMiddle3"},
}


def _rename(slot: str, naming: str) -> str:
    side = next((s for s in ("Left", "Right") if slot.startswith(s)), "")
    pattern = _NAMINGS[naming].get(slot[len(side):])
    return slot if pattern is None else pattern.format(Side=side, s=side[:1])


def humanoid_fixture_bones(fingers: bool = False, naming: str = "unity") -> list:
    """Bones of the humanoid test rig as (name, parent, head, tail), in a T-pose.

    21 bones; with fingers, 18 more (thumb, index, middle per hand). `naming` renames them to another
    convention ("unity", "blender" or "mixamo") without changing the skeleton.
    """
    bones = list(_HUMANOID_CENTRE)
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        for name, parent, head, tail in _HUMANOID_LEFT:
            parent = parent.replace("Left", side) if parent.startswith("Left") else parent
            bones.append((side + name, parent, (sign * head[0], head[1], head[2]), (sign * tail[0], tail[1], tail[2])))
        if fingers:
            hand_end = (sign * 0.77, 0.0, 1.38)
            for finger, offset, step in _FINGERS:
                start = (hand_end[0] + sign * offset[0], hand_end[1] + offset[1], hand_end[2] + offset[2])
                parent = side + "Hand"
                for segment in _SEGMENTS:
                    end = (start[0] + sign * step[0], start[1] + step[1], start[2] + step[2])
                    name = side + finger + segment
                    bones.append((name, parent, start, end))
                    parent, start = name, end
    return [(_rename(n, naming), _rename(p, naming) if p else None, h, tl) for n, p, h, tl in bones]


def build_humanoid_fixture(fingers: bool = False, naming: str = "unity") -> None:
    """A full humanoid test avatar in a T-pose, one box of mesh per bone.

    Used to compare Unity's automatic humanoid mapping with the audit's name matching.
    A nose on the head (towards -Y) keeps the figure asymmetric front to back.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    collection = bpy.context.scene.collection
    bones = humanoid_fixture_bones(fingers, naming)
    head_bone = _rename("Head", naming)

    arm_data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for name, parent, head, tail in bones:
        bone = arm_data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")

    vertices, faces, groups = [], [], {}
    for name, _parent, head, tail in bones:
        half = 0.006 if (tail_len := (Vector(tail) - Vector(head)).length) < 0.03 else 0.03
        head, tail = Vector(head), Vector(tail)
        along = (tail - head).normalized()
        side = along.cross(Vector((0, 0, 1)) if abs(along.z) < 0.9 else Vector((0, 1, 0))).normalized() * half
        up = along.cross(side).normalized() * half
        start = len(vertices)
        for end in (head, tail):
            vertices.extend([tuple(end - side - up), tuple(end + side - up), tuple(end + side + up), tuple(end - side + up)])
        quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        faces.extend(tuple(start + i for i in quad) for quad in quads)
        groups[name] = list(range(start, start + 8))
    nose = len(vertices)
    vertices.extend([(-0.02, -0.03, 1.58), (0.02, -0.03, 1.58), (0.0, -0.03, 1.63), (0.0, -0.10, 1.60)])
    faces.extend([(nose, nose + 1, nose + 3), (nose + 1, nose + 2, nose + 3), (nose + 2, nose, nose + 3)])
    groups[head_bone].extend(range(nose, nose + 4))

    mesh = bpy.data.meshes.new("Body")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    body = bpy.data.objects.new("Body", mesh)
    collection.objects.link(body)
    mesh.materials.append(bpy.data.materials.new("FixtureSkin"))
    for name, indices in groups.items():
        body.vertex_groups.new(name=name).add(indices, 1.0, "REPLACE")
    body.parent = arm
    body.modifiers.new("Armature", "ARMATURE").object = arm

    body.shape_key_add(name="Basis")
    key = body.shape_key_add(name="vrc.v_aa")
    key.data[nose + 3].co = Vector(vertices[nose + 3]) + Vector((0.0, -0.03, -0.01))
    key.value = 0.0
    bpy.context.view_layer.update()


FIXTURES = {
    "basic": build_fixture,
    "humanoid": build_humanoid_fixture,
    "humanoid_blender_names": lambda: build_humanoid_fixture(fingers=True, naming="blender"),
    "humanoid_mixamo_names": lambda: build_humanoid_fixture(fingers=True, naming="mixamo"),
}


def run_self_test(profile_path: Path) -> None:
    profile = load_profile(profile_path)
    checks = []

    def check(name: str, ok: bool, detail="") -> None:
        checks.append(name)
        print(f"  [{'ok' if ok else 'FAIL'}] {name}" + (f" - {detail}" if detail else ""))
        if not ok:
            raise AssertionError(f"{name}: {detail}")

    with tempfile.TemporaryDirectory(prefix="frosty_fbx_selftest_") as tmp:
        tmp = Path(tmp)

        # 1. A profile that leaves an option to Blender's default must be rejected.
        partial = json.loads(profile_path.read_text(encoding="utf-8"))
        del partial["export_scene_fbx"]["axis_up"]
        (tmp / "partial.json").write_text(json.dumps(partial), encoding="utf-8")
        try:
            load_profile(tmp / "partial.json")
            check("incomplete profile rejected", False, "profile without axis_up was accepted")
        except ExportError:
            check("incomplete profile rejected", True)

        # 2. Clean fixture: export, header, source untouched.
        build_fixture()
        expected = capture_metrics()
        report = export_fbx(profile, tmp / "fixture.fbx")
        check("clean fixture has no preflight issues", not report["issues"], report["issues"])
        check("source scene unchanged by export", report["source_unchanged"])
        check("FBX header matches profile", not report["header_problems"], report["header_problems"])
        print("  FBX header:", json.dumps(report["fbx"]["global_settings"]))
        print("  FBX object transforms:", json.dumps(report["fbx"]["models"]))

        check("fixture shape key exported with default weight 0",
              report["fbx"]["blend_shape_default_percent"] == {"vrc.v_aa": 0.0},
              report["fbx"]["blend_shape_default_percent"])

        # 2b. A shape key left switched on is reported, and its value does reach the file.
        key = bpy.data.objects["Body"].data.shape_keys.key_blocks["vrc.v_aa"]
        key.value = 1.0
        active_report = export_fbx(profile, tmp / "shape_on.fbx")
        check("non-zero shape key value reported and written as default weight 100",
              "shape_key_value_nonzero" in {i["code"] for i in active_report["issues"]}
              and active_report["fbx"]["blend_shape_default_percent"] == {"vrc.v_aa": 100.0}
              and key.value == 1.0, active_report["issues"])
        key.value = 0.0

        # 2c. The folder of the source .blend does not end up in the FBX.
        secret = tmp / "private_folder_name"
        secret.mkdir()
        bpy.ops.wm.save_as_mainfile(filepath=str(secret / "fixture_saved.blend"))
        stripped = export_fbx(profile, tmp / "stripped.fbx")
        data = (tmp / "stripped.fbx").read_bytes()
        check("source .blend folder blanked in the FBX, file name kept, file still readable",
              stripped["source_path_stripped"] and b"private_folder_name" not in data and b"fixture_saved.blend" in data
              and not stripped["header_problems"], stripped["source_path_stripped"])
        build_fixture()

        # 3. Existing output is not overwritten without the flag.
        try:
            export_fbx(profile, tmp / "fixture.fbx")
            check("existing output protected", False, "file was overwritten without --overwrite")
        except ExportError:
            check("existing output protected", True)

        # 4. The header check notices a wrong axis choice.
        wrong = json.loads(json.dumps(profile))
        wrong["export_scene_fbx"].update(axis_up="Z", axis_forward="Y")
        wrong_report = export_fbx(wrong, tmp / "wrong_axis.fbx")
        check("wrong up axis detected in header", bool(wrong_report["header_problems"]))

        # 5. Unapplied transforms are reported and left exactly as they were.
        body = bpy.data.objects["Body"]
        body.scale = (2.0, 2.0, 2.0)
        body.rotation_euler = (0.0, 0.0, math.radians(30))
        transformed_expected = capture_metrics()
        transformed = export_fbx(profile, tmp / "transformed.fbx")
        codes = {i["code"] for i in transformed["issues"]}
        check("unapplied scale and rotation reported",
              {"unapplied_object_scale", "unapplied_object_rotation"} <= codes, sorted(codes))
        check("unapplied transforms left in place",
              _round_list(body.scale, 4) == [2.0, 2.0, 2.0] and abs(body.rotation_euler[2] - math.radians(30)) < 1e-6)

        # 6. Round trips (each one resets the session, so they come last).
        trip = roundtrip_check(profile, tmp / "transformed.fbx", transformed_expected)
        check("transformed fixture round-trips in world space", trip["status"] == "pass", trip["failures"])

        # 7. The round-trip check notices a scale error (file written at 2x on purpose).
        build_fixture()
        doubled = json.loads(json.dumps(profile))
        doubled["export_scene_fbx"]["global_scale"] = 2.0
        export_fbx(doubled, tmp / "doubled.fbx")
        trip = roundtrip_check(profile, tmp / "doubled.fbx", expected)
        check("2x scale error detected by round trip", trip["status"] == "fail")

        trip = roundtrip_check(profile, tmp / "fixture.fbx", expected)
        check("clean fixture round-trips in world space", trip["status"] == "pass", trip["failures"])
        print("  Round-trip measurements:", json.dumps(trip["measured"]))

        # 8. The full humanoid test rig exports cleanly and round-trips.
        build_humanoid_fixture()
        humanoid_expected = capture_metrics()
        humanoid = export_fbx(profile, tmp / "humanoid.fbx")
        bone_models = [n for n, m in humanoid["fbx"]["models"].items() if m["class"] == "LimbNode"]
        check("humanoid fixture: 21 bones, no preflight issues, header as expected",
              len(bone_models) == 21 and not humanoid["issues"] and not humanoid["header_problems"],
              (len(bone_models), humanoid["issues"], humanoid["header_problems"]))
        trip = roundtrip_check(profile, tmp / "humanoid.fbx", humanoid_expected)
        check("humanoid fixture round-trips in world space", trip["status"] == "pass", trip["failures"])

        # 9. Naming variants with fingers: 39 bones each, exported and round-tripped.
        for variant in ("humanoid_blender_names", "humanoid_mixamo_names"):
            FIXTURES[variant]()
            variant_expected = capture_metrics()
            variant_report = export_fbx(profile, tmp / (variant + ".fbx"))
            limbs = [n for n, m in variant_report["fbx"]["models"].items() if m["class"] == "LimbNode"]
            trip = roundtrip_check(profile, tmp / (variant + ".fbx"), variant_expected)
            check(f"{variant}: 39 bones exported, no issues, round trip passes",
                  len(limbs) == 39 and not variant_report["issues"] and trip["status"] == "pass",
                  (len(limbs), variant_report["issues"], trip["failures"]))

    print(f"SELF-TEST PASS: {len(checks)} checks, profile {profile['profile']} v{profile['profile_version']}, "
          f"Blender {bpy.app.version_string}")


# -------------------------------------------------------------------------------- CLI

def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", type=Path, help="FBX file to write")
    parser.add_argument("--profile", type=Path, default=DEFAULT_PROFILE, help="Export profile JSON")
    parser.add_argument("--overwrite", action="store_true", help="Replace --out if it already exists")
    parser.add_argument("--roundtrip", action="store_true",
                        help="Re-import the FBX and compare with the source (discards the in-memory session)")
    parser.add_argument("--fixture", nargs="?", const="basic", choices=sorted(FIXTURES),
                        help="Export a generated test figure instead of the open file (use with --factory-startup): "
                             "'basic' (default, 4 bones), 'humanoid' (21 bones, T-pose), or 'humanoid_blender_names' / "
                             "'humanoid_mixamo_names' (39 bones with fingers, named in that convention)")
    parser.add_argument("--fixture-blend-out", type=Path,
                        help="With --fixture: also save the generated figure as a .blend (for running the audit on it)")
    parser.add_argument("--dry-run", action="store_true", help="Report settings and preflight issues; write nothing")
    parser.add_argument("--json-out", type=Path, help="Write the report JSON to this path")
    parser.add_argument("--self-test", action="store_true", help="Build a fixture and test the profile end to end")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(args)


def main() -> int:
    args = _arguments()
    try:
        if args.self_test:
            # Blender exits 0 after an uncaught Python error, so turn a failed check into exit code 1 here.
            try:
                run_self_test(args.profile)
            except Exception:                            # a failed check or a crash: both fail the self-test
                traceback.print_exc()
                print("SELF-TEST FAILED")
                return 1
            return 0
        profile = load_profile(args.profile)
        if args.fixture:
            if bpy.data.filepath:
                raise ExportError("--fixture replaces the session with the test figure; do not combine it with a .blend.")
            FIXTURES[args.fixture]()
            if args.fixture_blend_out:
                target = args.fixture_blend_out.resolve()
                if target.suffix.lower() != ".blend" or (target.exists() and not args.overwrite):
                    raise ExportError(f"--fixture-blend-out must be a new .blend file (or pass --overwrite): {target}")
                target.parent.mkdir(parents=True, exist_ok=True)
                # copy=True writes the file without making it the session's own file.
                bpy.ops.wm.save_as_mainfile(filepath=str(target), copy=True)
        report = {"schema_version": 1, "tool": "frosty-neon-blender-fbx-export",
                  "blender_version": bpy.app.version_string, "blend_file": bpy.data.filepath or None,
                  "profile": profile["profile"], "profile_version": profile["profile_version"],
                  "export_settings": profile["export_scene_fbx"]}
        if args.dry_run:
            report["issues"] = preflight(profile)
            report["status"] = "fail" if any(i["severity"] == "error" for i in report["issues"]) else "dry-run"
        else:
            if not args.out:
                raise ExportError("Pass --out <file.fbx>, or --dry-run / --self-test.")
            expected = capture_metrics() if args.roundtrip else None
            report.update(export_fbx(profile, args.out, args.overwrite))
            failed = bool(report["header_problems"])
            if args.roundtrip:
                report["roundtrip"] = roundtrip_check(profile, Path(report["output"]), expected)
                failed = failed or report["roundtrip"]["status"] == "fail"
            report["status"] = "fail" if failed else ("warn" if report["issues"] else "pass")
            report["limitations"] = [
                "Blender round trip only; Unity import of this file has not been verified by this tool.",
                "Not a VRChat SDK build, humanoid mapping, or performance-rank validation.",
            ]
    except ExportError as error:
        print(f"EXPORT STOPPED: {error}")
        if args.json_out:                                # so the caller's "see the report" points at the reason
            args.json_out.parent.mkdir(parents=True, exist_ok=True)
            args.json_out.write_text(json.dumps({"schema_version": 1, "tool": "frosty-neon-blender-fbx-export",
                                                 "status": "fail", "error": str(error)}, indent=2) + "\n",
                                     encoding="utf-8")
        return 1
    payload = json.dumps(report, indent=2)
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(payload + "\n", encoding="utf-8")
    print(payload)
    return 1 if report["status"] == "fail" else 0


if __name__ == "__main__":
    raise SystemExit(main())
