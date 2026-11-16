"""Read-only, standalone Blender scene preflight for avatar source files.

Run with:
  blender --background file.blend --python audit_avatar_scene.py -- --json-out report.json
  blender --background --factory-startup --python audit_avatar_scene.py -- --self-test

The checks are advisory and are not a VRChat SDK validation or humanoid-rig certification.
The humanoid section compares bone names with the table in data/humanoid_bones_v1.json and
reports what it recognises; it never renames bones. See docs/HUMANOID_BONE_REPORT.md.
The performance section estimates a VRChat rank for PC and Quest separately from counts taken
in Blender, using data/performance_budgets_v1.json. See docs/PERFORMANCE_BUDGETS.md.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
import traceback
from pathlib import Path

import bpy

DEFAULT_BONE_TABLE = Path(__file__).resolve().parent / "data" / "humanoid_bones_v1.json"
DEFAULT_BUDGETS = Path(__file__).resolve().parent / "data" / "performance_budgets_v1.json"


# ------------------------------------------------------------------ humanoid bone names

def split_bone_name(name: str, table: dict) -> tuple:
    """Return (side, core) for a bone name.

    side is 'left', 'right' or None; core is the name without its side marker,
    lower-cased, letters and digits only.
      'mixamorig:LeftForeArm' -> ('left', 'forearm')
      'upper_arm.R'           -> ('right', 'upperarm')
    """
    text = name.split(":")[-1]
    for side, words in table["side_words"].items():
        for word in words:
            match = re.search(word, text, flags=re.IGNORECASE)
            if match:
                return side, _core(text[:match.start()] + text[match.end():])
    for side, letter in table["side_letters"].items():
        match = (re.search(rf"[._\- ]{letter}$", text, flags=re.IGNORECASE)
                 or re.search(rf"^{letter}[._\- ]", text, flags=re.IGNORECASE))
        if match:
            return side, _core(text[:match.start()] + text[match.end():])
    return None, _core(text)


def _core(text: str) -> str:
    return re.sub(r"[^a-z0-9]", "", text.lower())


def humanoid_slots(table: dict) -> list:
    """Expand the table into one row per Unity humanoid slot (e.g. LeftUpperArm)."""
    fingers = table["fingers"]
    segments = list(fingers["segments"])
    sided = {b["slot"] for b in table["bones"] if b["sided"]} | {f + s for f in fingers["names"] for s in segments}
    slots = []

    def add(slot: str, side, parent, aliases: list, unity_required=False, vrchat_required=False) -> None:
        prefix = side.capitalize() if side else ""
        slots.append({"slot": prefix + slot, "side": side, "aliases": aliases,
                      "parent": ((prefix if parent in sided else "") + parent) if parent else None,
                      "unity_required": unity_required, "vrchat_required": vrchat_required})

    for row in table["bones"]:
        for side in (("left", "right") if row["sided"] else (None,)):
            add(row["slot"], side, row["parent"], row["aliases"], row["unity_required"], row["vrchat_required"])
    for finger, finger_aliases in fingers["names"].items():
        for index, segment in enumerate(segments):
            aliases = [prefix + f + s for prefix in fingers["optional_prefixes"]
                       for f in finger_aliases for s in fingers["segments"][segment]]
            parent = fingers["parent"] if index == 0 else finger + segments[index - 1]
            for side in ("left", "right"):
                add(finger + segment, side, parent, aliases)
    return slots


def humanoid_bone_report(armature_obj, table: dict) -> tuple:
    """Match one armature's bone names against the humanoid slots. Returns (section, issues)."""
    bones = armature_obj.data.bones
    parsed = {b.name: split_bone_name(b.name, table) for b in bones}
    slots = humanoid_slots(table)
    by_slot = {s["slot"]: s for s in slots}
    matched, claimed, ambiguous = {}, set(), {}

    # Slots are tried in table order and aliases in listed order, so a specific name
    # ('upleg') wins over a vague one ('leg') and a bone is used for one slot only.
    for slot in slots:
        for alias in slot["aliases"]:
            found = [n for n, (side, core) in parsed.items()
                     if n not in claimed and side == slot["side"] and core == alias]
            if len(found) == 1:
                matched[slot["slot"]] = found[0]
                claimed.add(found[0])
                break
            if len(found) > 1:
                ambiguous[slot["slot"]] = sorted(found)
                break

    def ancestors(bone_name: str) -> list:
        chain, bone = [], bones[bone_name].parent
        while bone:
            chain.append(bone.name)
            bone = bone.parent
        return chain

    issues = []

    def issue(severity: str, code: str, message: str, **extra) -> None:
        issues.append({"severity": severity, "code": code, "object": armature_obj.name, "message": message, **extra})

    section = {
        "armature": armature_obj.name,
        "bone_count": len(bones),
        "matched": matched,
        "ambiguous": ambiguous,
        "unity_required_status": table["unity_required_status"],
        "missing_unity_required": [s["slot"] for s in slots if s["unity_required"] and s["slot"] not in matched],
        "missing_vrchat_required": [s["slot"] for s in slots if s["vrchat_required"] and s["slot"] not in matched],
        "unmatched_bones": sorted(n for n in parsed if n not in claimed),
    }

    if not matched:
        issue("info", "no_humanoid_names",
              "No bone name matches a humanoid slot. Fine for a non-humanoid (Generic) rig; for a humanoid, "
              "the names are outside the table and Unity's own mapping will need checking by hand.")
        return section, issues

    for slot_name, names in ambiguous.items():
        issue("warning", "humanoid_ambiguous_names",
              f"Several bones look like {slot_name}: {names}. None was chosen.", slot=slot_name)
    if section["missing_unity_required"]:
        issue("warning", "humanoid_missing_unity_required",
              f"No bone recognised for slots Unity needs for a humanoid: {section['missing_unity_required']}. "
              f"Required-slot list status: {table['unity_required_status']}.")
    vrchat_only = [s for s in section["missing_vrchat_required"] if s not in section["missing_unity_required"]]
    if vrchat_only:
        issue("warning", "humanoid_missing_vrchat_required",
              f"No bone recognised for slots VRChat's rig requirements ask for: {vrchat_only}.")

    hips = matched.get("Hips")
    outside = set()
    if hips:
        outside = {s for s, n in matched.items() if s != "Hips" and hips not in ancestors(n)}
        if outside:
            issue("warning", "humanoid_split_hierarchy",
                  f"Hips bone '{hips}' is not an ancestor of the bones matched to: {sorted(outside)}.")
    for slot_name, bone_name in matched.items():
        parent_slot = by_slot[slot_name]["parent"]
        while parent_slot and parent_slot not in matched:       # skip optional slots that are absent
            parent_slot = by_slot[parent_slot]["parent"]
        if parent_slot and slot_name not in outside and matched[parent_slot] not in ancestors(bone_name):
            issue("warning", "humanoid_unexpected_parent",
                  f"'{bone_name}' ({slot_name}) is not below '{matched[parent_slot]}' ({parent_slot}).",
                  slot=slot_name)

    # The rule as the VRChat SDK enforces it (SDK 3.10.6, VRCSdkControlPanelAvatarBuilder.AnalyzeIK,
    # read 2026-11-13): if UpperChest is mapped, both shoulders and the neck must be direct children
    # of UpperChest; otherwise of Chest. Its message: "Make sure that the parent of both Shoulders and
    # the Neck is the Chest (or UpperChest if set)." The older Rig Requirements web page names only Chest.
    upper_slot = "UpperChest" if matched.get("UpperChest") else "Chest"
    upper = matched.get(upper_slot)
    if upper:
        for slot_name in ("LeftShoulder", "RightShoulder", "Neck"):
            bone_name = matched.get(slot_name)
            parent = bones[bone_name].parent if bone_name else None
            if not parent or parent.name != upper:
                parent_name = parent.name if parent else "no parent"
                issue("warning", "humanoid_chest_children",
                      f"'{bone_name}' ({slot_name}) has parent '{parent_name}', not directly of the {upper_slot} "
                      f"bone '{upper}'. The VRChat SDK (3.10.6) expects shoulders and neck directly under the "
                      "Chest, or under the UpperChest when one is mapped.",
                      slot=slot_name)
    for side in ("Left", "Right"):
        for parent_slot, child_slot in (("UpperArm", "LowerArm"), ("LowerArm", "Hand"),
                                        ("UpperLeg", "LowerLeg"), ("LowerLeg", "Foot")):
            parent_name, child_name = matched.get(side + parent_slot), matched.get(side + child_slot)
            if parent_name and child_name and len(bones[parent_name].children) > 1:
                issue("info", "humanoid_extra_limb_children",
                      f"'{parent_name}' has {len(bones[parent_name].children)} child bones. VRChat's IK uses the "
                      f"first child, which should be '{child_name}'; check the child order in Unity after import.",
                      slot=side + parent_slot)
    return section, issues


# ------------------------------------------------------------------ performance budgets

def rank_for(value: int, limits: dict, table: dict) -> str:
    """Best rank whose maximum is not exceeded, e.g. 32,001 triangles on PC -> 'Good'."""
    for rank in table["ranks"]:
        if value <= limits[rank]:
            return rank
    return table["rank_above_all_limits"]


ZERO_AREA_M2 = 1e-12


def count_zero_area_triangles(mesh) -> int:
    """Triangles with no area, e.g. a quad with two corners at the same position.

    Measured on FrostyNeonBot (2026-11-14): Blender counted 3,696 triangles of which 144 had zero
    area, and the VRChat SDK counted 3,552 for the imported model. Such triangles do not reach
    Unity's mesh, so they are left out of the rank estimate and reported separately.
    """
    mesh.calc_loop_triangles()
    return sum(1 for triangle in mesh.loop_triangles if triangle.area < ZERO_AREA_M2)


def measure_performance_stats(meshes: list, armatures: list) -> dict:
    """Count the statistics that can be estimated from the Blender scene. Read-only.

    'triangles' excludes zero-area triangles; see count_zero_area_triangles.
    """
    stats = {"triangles": 0, "skinned_meshes": 0, "basic_meshes": 0, "material_slots": 0,
             "bones": sum(len(arm.data.bones) for arm in armatures)}
    for obj in meshes:
        mesh = obj.data
        mesh.calc_loop_triangles()
        stats["triangles"] += len(mesh.loop_triangles) - count_zero_area_triangles(mesh)
        skinned = any(mod.type == "ARMATURE" for mod in obj.modifiers) or bool(mesh.shape_keys)
        stats["skinned_meshes" if skinned else "basic_meshes"] += 1
        if mesh.polygons:
            indices = [0] * len(mesh.polygons)
            mesh.polygons.foreach_get("material_index", indices)
            stats["material_slots"] += max(1, len(set(indices)))
    return stats


def performance_report(meshes: list, armatures: list, table: dict, platforms=None, targets=None) -> tuple:
    """Estimate a VRChat performance rank per platform. Returns (section, issues)."""
    targets = targets or {}
    all_ranks = table["ranks"] + [table["rank_above_all_limits"]]
    chosen = list(platforms) if platforms else list(table["platforms"])
    for name in list(chosen) + list(targets):
        if name not in table["platforms"]:
            raise ValueError(f"Unknown platform '{name}'. Known: {sorted(table['platforms'])}")
    for rank in targets.values():
        if rank not in all_ranks:
            raise ValueError(f"Unknown rank '{rank}'. Known: {all_ranks}")

    stats = measure_performance_stats(meshes, armatures)
    section = {"budget_table": f"{table['table']} v{table['table_version']}",
               "limits_source": table["source"]["page"], "limits_read_on": table["source"]["read_on"],
               "estimate_only": True, "measured": stats,
               "zero_area_triangles_excluded": sum(count_zero_area_triangles(obj.data) for obj in meshes),
               "platforms": {},
               "not_estimated": table["not_estimated"]}
    issues = []
    for name in chosen:
        platform = table["platforms"][name]
        per_stat = {stat: rank_for(value, platform["limits"][stat], table) for stat, value in stats.items()}
        worst = max(per_stat.values(), key=all_ranks.index)
        target = targets.get(name, platform["default_target_rank"])
        over = sorted(stat for stat, rank in per_stat.items() if all_ranks.index(rank) > all_ranks.index(target))
        section["platforms"][name] = {
            "label": platform["label"], "estimated_rank": worst, "target_rank": target,
            "meets_target": not over,
            "statistics": {stat: {"value": stats[stat], "rank": per_stat[stat],
                                  "target_maximum": platform["limits"][stat].get(target)}
                           for stat in stats},
        }
        if over:
            detail = ", ".join(f"{stat} {stats[stat]} (maximum for {target}: {platform['limits'][stat][target]}, "
                               f"rank {per_stat[stat]})" for stat in over)
            issues.append({"severity": "warning", "code": "performance_below_target", "platform": name,
                           "message": f"{platform['label']}: estimated rank {worst}, target {target}. Over budget: "
                                      f"{detail}. Estimate from the Blender scene only; the SDK build panel decides."})
    return section, issues


# --------------------------------------------------------------------------- scene audit

def _close_to_one(value: float, tolerance: float = 1e-4) -> bool:
    return abs(value - 1.0) <= tolerance


def audit_scene(bone_table_path: Path = DEFAULT_BONE_TABLE, budgets_path: Path = DEFAULT_BUDGETS,
                platforms=None, targets=None) -> dict:
    bone_table = json.loads(Path(bone_table_path).read_text(encoding="utf-8"))
    budgets = json.loads(Path(budgets_path).read_text(encoding="utf-8"))
    objects = list(bpy.context.scene.objects)
    meshes = [obj for obj in objects if obj.type == "MESH"]
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    issues = []
    mesh_rows = []
    total_triangles = 0
    total_zero_area = 0

    if not meshes:
        issues.append({"severity": "error", "code": "no_mesh", "message": "Scene contains no mesh objects."})
    if not armatures:
        issues.append({"severity": "error", "code": "no_armature", "message": "Scene contains no armature object."})

    for obj in meshes:
        mesh = obj.data
        mesh.calc_loop_triangles()
        triangles = len(mesh.loop_triangles)
        total_triangles += triangles
        zero_area = count_zero_area_triangles(mesh)
        total_zero_area += zero_area
        if zero_area:
            issues.append({"severity": "info", "code": "zero_area_triangles", "object": obj.name,
                           "message": f"{zero_area} of {triangles} triangles have no area (corners at the same "
                                      "position). They are not kept in Unity's imported mesh, so the VRChat SDK "
                                      f"is expected to count {triangles - zero_area}. They are excluded from the "
                                      "rank estimate. Harmless, but merging the doubled vertices cleans them up."})
        scale = [float(x) for x in obj.scale]
        if not all(_close_to_one(x) for x in scale):
            issues.append({
                "severity": "warning", "code": "unapplied_object_scale",
                "object": obj.name,
                "message": "Object scale is not (1,1,1); review before export. Audit does not apply transforms.",
            })
        if not any(mod.type == "ARMATURE" for mod in obj.modifiers):
            issues.append({"severity": "warning", "code": "no_armature_modifier", "object": obj.name,
                           "message": "Mesh has no Armature modifier; this may be intentional for rigid accessories."})
        if not obj.material_slots:
            issues.append({"severity": "warning", "code": "no_material_slots", "object": obj.name,
                           "message": "Mesh has no material slots."})
        mesh_rows.append({"name": obj.name, "vertices": len(mesh.vertices), "polygons": len(mesh.polygons),
                          "triangles": triangles, "zero_area_triangles": zero_area, "scale": scale,
                          "armature_modifier": any(mod.type == "ARMATURE" for mod in obj.modifiers),
                          "shape_key_blocks": len(mesh.shape_keys.key_blocks) if mesh.shape_keys else 0,
                          "material_slots": len(obj.material_slots)})

    image_rows = []
    seen = set()
    for material in bpy.data.materials:
        if not material.node_tree:
            continue
        for node in material.node_tree.nodes:
            if node.type != "TEX_IMAGE" or not node.image or node.image.as_pointer() in seen:
                continue
            image = node.image
            seen.add(image.as_pointer())
            packed = bool(image.packed_file or image.packed_files)
            resolved = bpy.path.abspath(image.filepath) if image.filepath else ""
            exists = bool(resolved and Path(resolved).is_file())
            row = {"name": image.name, "filepath": image.filepath, "packed": packed,
                   "exists": exists if image.filepath else None,
                   "size": [int(image.size[0]), int(image.size[1])]}
            image_rows.append(row)
            if image.filepath and not packed and not exists:
                issues.append({"severity": "error", "code": "missing_image", "object": image.name,
                               "message": f"Image path cannot be resolved: {image.filepath}"})

    humanoid_rows = []
    for arm in armatures:
        section, bone_issues = humanoid_bone_report(arm, bone_table)
        humanoid_rows.append(section)
        issues.extend(bone_issues)

    performance, performance_issues = performance_report(meshes, armatures, budgets, platforms, targets)
    issues.extend(performance_issues)

    scale_length = float(bpy.context.scene.unit_settings.scale_length)
    if not math.isfinite(scale_length) or scale_length <= 0:
        issues.append({"severity": "error", "code": "invalid_scene_unit_scale",
                       "message": "Scene unit scale must be a positive finite value."})

    # 'info' entries are notes and do not change the status.
    status = "fail" if any(issue["severity"] == "error" for issue in issues) else (
        "warn" if any(issue["severity"] == "warning" for issue in issues) else "pass")
    return {
        "schema_version": 4,
        "tool": "frosty-neon-blender-avatar-audit",
        "blender_version": bpy.app.version_string,
        "blend_file": bpy.data.filepath or None,
        "status": status,
        "scene": bpy.context.scene.name,
        "scene_unit_scale": scale_length,
        "object_counts": {"total": len(objects), "meshes": len(meshes), "armatures": len(armatures)},
        "triangle_count": total_triangles,
        "zero_area_triangle_count": total_zero_area,
        "meshes": mesh_rows,
        "images": image_rows,
        "humanoid_bone_table": f"{bone_table['table']} v{bone_table['table_version']}",
        "humanoid_bones": humanoid_rows,
        "performance": performance,
        "issues": issues,
        "limitations": [
            "Advisory source-scene checks only; not a VRChat SDK build or humanoid mapping validation.",
            "Does not alter transforms, geometry, materials, or files and does not export FBX.",
            "Blender-to-Unity axis and scale conversion depends on export/import settings and is not certified here.",
            "Humanoid bones are matched by name against a table of common conventions. Unity's Avatar "
            "configuration decides the real mapping; a clean report here does not guarantee it.",
            "Performance ranks are estimates from five statistics counted in Blender. PC and Quest are judged "
            "separately; texture memory, PhysBones and other Unity-side statistics are not estimated. The VRChat "
            "SDK build panel gives the real rank.",
        ],
    }


# ------------------------------------------------------------------------------ self-test

# Test skeleton as (Unity slot, parent slot); each naming convention spells the same bones differently.
_LIMB = (("Shoulder", "Chest"), ("UpperArm", "Shoulder"), ("LowerArm", "UpperArm"), ("Hand", "LowerArm"),
         ("UpperLeg", "Hips"), ("LowerLeg", "UpperLeg"), ("Foot", "LowerLeg"), ("Toes", "Foot"))
_TEST_SKELETON = [("Hips", None), ("Spine", "Hips"), ("Chest", "Spine"), ("Neck", "Chest"), ("Head", "Neck")] + [
    (side + slot, parent if parent in ("Chest", "Hips") else side + parent)
    for side in ("Left", "Right") for slot, parent in _LIMB]
_TEST_NAMES = {
    "unity": {},
    "blender": {"Hips": "hips", "Spine": "spine", "Chest": "chest", "Neck": "neck", "Head": "head",
                "Shoulder": "shoulder.{s}", "UpperArm": "upper_arm.{s}", "LowerArm": "forearm.{s}",
                "Hand": "hand.{s}", "UpperLeg": "thigh.{s}", "LowerLeg": "shin.{s}", "Foot": "foot.{s}",
                "Toes": "toe.{s}"},
    "mixamo": {"Hips": "mixamorig:Hips", "Spine": "mixamorig:Spine", "Chest": "mixamorig:Spine1",
               "Neck": "mixamorig:Neck", "Head": "mixamorig:Head", "Shoulder": "mixamorig:{Side}Shoulder",
               "UpperArm": "mixamorig:{Side}Arm", "LowerArm": "mixamorig:{Side}ForeArm",
               "Hand": "mixamorig:{Side}Hand", "UpperLeg": "mixamorig:{Side}UpLeg",
               "LowerLeg": "mixamorig:{Side}Leg", "Foot": "mixamorig:{Side}Foot",
               "Toes": "mixamorig:{Side}ToeBase"},
    "mmd_english": {"Shoulder": "{Side} shoulder", "UpperArm": "{Side} arm", "LowerArm": "{Side} elbow",
                    "Hand": "{Side} wrist", "UpperLeg": "{Side} leg", "LowerLeg": "{Side} knee",
                    "Foot": "{Side} ankle", "Toes": "{Side} toe"},
}


def _test_name(convention: str, slot: str) -> str:
    side = next((s for s in ("Left", "Right") if slot.startswith(s)), "")
    pattern = _TEST_NAMES[convention].get(slot[len(side):])
    return slot if pattern is None else pattern.format(Side=side, s=side[:1])


def _build_test_rig(object_name: str, convention: str, drop=(), reparent=None, extra=()):
    """Create an armature for the self-test. `reparent` maps slot -> new parent slot (None = no parent)."""
    reparent = reparent or {}
    arm_data = bpy.data.armatures.new(object_name)
    arm = bpy.data.objects.new(object_name, arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    rows = [(slot, reparent[slot] if slot in reparent else parent)
            for slot, parent in _TEST_SKELETON if slot not in drop]
    rows = [(_test_name(convention, s), _test_name(convention, p) if p else None) for s, p in rows] + list(extra)
    for index, (name, _) in enumerate(rows):
        bone = arm_data.edit_bones.new(name)
        bone.head, bone.tail = (index * 0.1, 0, 0), (index * 0.1, 0, 0.1)
    for name, parent in rows:
        if parent and parent in arm_data.edit_bones:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def _build_performance_fixture(triangles: int, used_materials: int, bones: int, basic_meshes: int = 0) -> None:
    """Replace the scene with one skinned mesh of exactly `triangles` triangles plus optional rigid meshes."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    arm_data = bpy.data.armatures.new("BudgetRig")
    arm = bpy.data.objects.new("BudgetRig", arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for index in range(bones):
        bone = arm_data.edit_bones.new(f"Bone{index:03d}")
        bone.head, bone.tail = (index * 0.01, 0, 0), (index * 0.01, 0, 0.1)
    bpy.ops.object.mode_set(mode="OBJECT")

    def add_mesh(name: str, count: int, material_count: int, unused_slots: int = 0):
        vertices = [(i + dx, dy, 0.0) for i in range(count) for dx, dy in ((0, 0), (0.5, 0), (0, 0.5))]
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(count)])
        for index in range(material_count + unused_slots):
            mesh.materials.append(bpy.data.materials.new(f"{name}_Mat{index}"))
        mesh.polygons.foreach_set("material_index", [i % material_count for i in range(count)])
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj

    body = add_mesh("BudgetBody", triangles - basic_meshes, used_materials - basic_meshes, unused_slots=1)
    body.modifiers.new("Armature", "ARMATURE").object = arm
    for index in range(basic_meshes):
        add_mesh(f"BudgetProp{index}", 1, 1)


def run_self_test() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    arm_data = bpy.data.armatures.new("TestRig")
    arm = bpy.data.objects.new("TestRig", arm_data)
    bpy.context.collection.objects.link(arm)
    mesh_data = bpy.data.meshes.new("TestMesh")
    mesh_data.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    mesh_obj = bpy.data.objects.new("TestMesh", mesh_data)
    bpy.context.collection.objects.link(mesh_obj)
    material = bpy.data.materials.new("TestMaterial")
    mesh_obj.data.materials.append(material)
    modifier = mesh_obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    minimal = audit_scene()
    assert minimal["status"] == "pass", minimal
    assert minimal["object_counts"]["meshes"] == 1, minimal
    assert minimal["object_counts"]["armatures"] == 1, minimal
    assert minimal["triangle_count"] == 1, minimal
    assert minimal["meshes"][0]["armature_modifier"] is True, minimal
    assert [i["code"] for i in minimal["issues"]] == ["no_humanoid_names"], minimal["issues"]
    print("  [ok] minimal fixture: status pass; boneless rig noted as non-humanoid (info only)")

    table = json.loads(DEFAULT_BONE_TABLE.read_text(encoding="utf-8"))
    assert split_bone_name("mixamorig:LeftForeArm", table) == ("left", "forearm")
    assert split_bone_name("upper_arm.R", table) == ("right", "upperarm")
    assert split_bone_name("L_Hand", table) == ("left", "hand")
    assert split_bone_name("Tail", table) == (None, "tail")
    print("  [ok] side and name parsing")

    for convention in sorted(_TEST_NAMES):
        rig = _build_test_rig(f"Rig_{convention}", convention)
        section, bone_issues = humanoid_bone_report(rig, table)
        expected = {slot: _test_name(convention, slot) for slot, _ in _TEST_SKELETON}
        assert section["matched"] == expected, (convention, section["matched"])
        assert not section["missing_unity_required"] and not section["missing_vrchat_required"], section
        assert not section["unmatched_bones"] and not bone_issues, (convention, section, bone_issues)
        assert sorted(b.name for b in rig.data.bones) == sorted(expected.values()), convention
        print(f"  [ok] {convention} naming: {len(expected)} bones matched to the right slots, no issues, names untouched")

    # Fingers, in two spellings.
    rig = _build_test_rig("Rig_fingers", "unity", extra=[
        ("LeftThumbProximal", "LeftHand"), ("LeftThumbIntermediate", "LeftThumbProximal"),
        ("mixamorig:RightHandIndex1", "RightHand"), ("pinky.02.R", "RightHand")])
    section, bone_issues = humanoid_bone_report(rig, table)
    assert section["matched"]["LeftThumbIntermediate"] == "LeftThumbIntermediate", section["matched"]
    assert section["matched"]["RightIndexProximal"] == "mixamorig:RightHandIndex1", section["matched"]
    assert section["matched"]["RightLittleIntermediate"] == "pinky.02.R", section["matched"]
    assert not section["unmatched_bones"], section["unmatched_bones"]
    print("  [ok] finger bones matched in Unity, Mixamo and Blender spellings")

    # A rig with deliberate faults must produce the specific findings.
    rig = _build_test_rig("Rig_faulty", "unity", drop={"Neck", "RightFoot"},
                          reparent={"LeftShoulder": "Spine", "LeftUpperLeg": None, "Head": "Chest",
                                    "RightHand": "RightUpperArm", "RightToes": "RightLowerLeg"},
                          extra=[("LeftElbowTwist", "LeftUpperArm"), ("Tail", "Hips")])
    section, bone_issues = humanoid_bone_report(rig, table)
    codes = sorted({i["code"] for i in bone_issues})
    assert section["missing_unity_required"] == ["RightFoot"], section
    assert "Neck" in section["missing_vrchat_required"], section
    assert section["unmatched_bones"] == ["LeftElbowTwist", "Tail"], section
    assert codes == ["humanoid_chest_children", "humanoid_extra_limb_children", "humanoid_missing_unity_required",
                     "humanoid_missing_vrchat_required", "humanoid_split_hierarchy",
                     "humanoid_unexpected_parent"], codes
    print(f"  [ok] faulty rig: reported {codes}")
    missing_message = next(i["message"] for i in bone_issues if i["code"] == "humanoid_missing_unity_required")
    status = table["unity_required_status"]
    assert section["unity_required_status"] == status and status in missing_message, missing_message
    assert status.startswith("verified in Unity 2022.3"), status
    # The list Unity 2022.3.22f1 itself returned from HumanTrait.RequiredBone (see docs/UNITY_VALIDATION.md).
    unity_required = {"Hips", "Spine", "Head"} | {side + part for side in ("Left", "Right") for part in (
        "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot")}
    slots = humanoid_slots(table)
    assert {s["slot"] for s in slots if s["unity_required"]} == unity_required and len(slots) == 55, len(slots)
    print("  [ok] required-slot list equals Unity's 15 required bones (55 slots in total); status shown in report and finding")

    # The SDK 3.10.6 rule: with UpperChest mapped, shoulders and neck belong directly under UpperChest.
    rig = _build_test_rig("Rig_upperchest_between", "unity", extra=[("UpperChest", "Chest")],
                          reparent={"Neck": "UpperChest", "LeftShoulder": "UpperChest", "RightShoulder": "UpperChest"})
    section, bone_issues = humanoid_bone_report(rig, table)
    assert section["matched"]["UpperChest"] == "UpperChest" and not bone_issues, bone_issues
    print("  [ok] UpperChest mapped, shoulders and neck under UpperChest: no findings (SDK 3.10.6 rule)")

    # A mapped shoulder or neck without any parent also fails the SDK's direct-parent condition.
    rig = _build_test_rig("Rig_upperchest_orphan", "unity", extra=[("UpperChest", "Chest")],
                          reparent={"Neck": None, "LeftShoulder": "UpperChest", "RightShoulder": "UpperChest"})
    section, bone_issues = humanoid_bone_report(rig, table)
    assert [i["slot"] for i in bone_issues if i["code"] == "humanoid_chest_children"] == ["Neck"], bone_issues
    print("  [ok] UpperChest mapped, neck has no parent: neck reported")

    # UpperChest mapped, but shoulders and neck left under Chest: the SDK would reject this, so report all three.
    rig = _build_test_rig("Rig_upperchest_beside", "unity", extra=[("UpperChest", "Chest")])
    section, bone_issues = humanoid_bone_report(rig, table)
    assert section["matched"]["UpperChest"] == "UpperChest", section["matched"]
    assert {i["code"] for i in bone_issues} == {"humanoid_chest_children"}, bone_issues
    assert sorted(i["slot"] for i in bone_issues) == ["LeftShoulder", "Neck", "RightShoulder"], bone_issues
    assert all("UpperChest bone 'UpperChest'" in i["message"] for i in bone_issues), bone_issues
    print("  [ok] UpperChest mapped, shoulders and neck under Chest: all three reported against UpperChest")

    rig = _build_test_rig("Rig_ambiguous", "unity", extra=[("Head.001", "Neck"), ("head", "Neck")])
    rig.data.bones["Head"].name = "HEAD"
    section, bone_issues = humanoid_bone_report(rig, table)
    assert "Head" in section["ambiguous"] and "Head" not in section["matched"], section
    assert "humanoid_ambiguous_names" in {i["code"] for i in bone_issues}
    print("  [ok] two bones with the same recognised name are reported, neither chosen")

    full = audit_scene()
    assert full["status"] == "warn" and len(full["humanoid_bones"]) == 7 + len(_TEST_NAMES), full["status"]
    print("  [ok] full audit covers every armature and ends in status warn")

    # ---- performance budgets
    budgets = json.loads(DEFAULT_BUDGETS.read_text(encoding="utf-8"))
    pc, quest = budgets["platforms"]["pc"]["limits"], budgets["platforms"]["quest"]["limits"]
    assert minimal["performance"]["platforms"]["pc"]["estimated_rank"] == "Excellent", minimal["performance"]
    assert minimal["performance"]["platforms"]["quest"]["estimated_rank"] == "Excellent", minimal["performance"]
    # Spot-check the table against the numbers on VRChat's page, then the rank boundaries.
    assert (pc["triangles"]["Excellent"], pc["triangles"]["Poor"], pc["bones"]["Poor"]) == (32000, 70000, 400)
    assert (quest["triangles"]["Excellent"], quest["triangles"]["Poor"], quest["material_slots"]["Poor"]) == (7500, 20000, 4)
    boundaries = [(pc, "triangles", 32000, "Excellent"), (pc, "triangles", 32001, "Good"),
                  (pc, "triangles", 70000, "Good"), (pc, "triangles", 70001, "Very Poor"),
                  (quest, "triangles", 7500, "Excellent"), (quest, "triangles", 7501, "Good"),
                  (quest, "triangles", 20000, "Poor"), (quest, "triangles", 20001, "Very Poor"),
                  (quest, "bones", 150, "Medium"), (quest, "bones", 151, "Very Poor"),
                  (pc, "skinned_meshes", 2, "Good"), (quest, "skinned_meshes", 2, "Medium")]
    for limits, stat, value, expected_rank in boundaries:
        assert rank_for(value, limits[stat], budgets) == expected_rank, (stat, value, expected_rank)
    print(f"  [ok] minimal fixture ranks Excellent on PC and Quest; {len(boundaries)} rank boundaries correct")

    # One skinned mesh, 7,501 triangles, 2 used + 1 unused material slot, 91 bones.
    _build_performance_fixture(triangles=7501, used_materials=2, bones=91)
    report = audit_scene()
    perf = report["performance"]
    assert perf["measured"] == {"triangles": 7501, "skinned_meshes": 1, "basic_meshes": 0,
                                "material_slots": 2, "bones": 91}, perf["measured"]
    assert report["meshes"][0]["material_slots"] == 3, report["meshes"]       # the unused slot is not budgeted
    assert (perf["platforms"]["pc"]["estimated_rank"], perf["platforms"]["quest"]["estimated_rank"]) == ("Good", "Medium")
    assert not [i for i in report["issues"] if i["code"] == "performance_below_target"], report["issues"]
    print("  [ok] 7,501-triangle fixture: counts exact, PC Good and Quest Medium, both on target, no finding")

    # Same avatar judged against stricter targets, and with the platform list narrowed.
    report = audit_scene(targets={"pc": "Excellent", "quest": "Good"})
    found = {i["platform"]: i["message"] for i in report["issues"] if i["code"] == "performance_below_target"}
    assert sorted(found) == ["pc", "quest"] and "bones 91" in found["pc"] and "triangles" not in found["pc"], found
    assert "material_slots 2" in found["quest"] and "bones 91" in found["quest"], found
    report = audit_scene(platforms=["pc"])
    assert list(report["performance"]["platforms"]) == ["pc"], report["performance"]["platforms"]
    print("  [ok] target ranks and platform list are configurable; findings name the statistics over budget")

    # A PC-sized avatar: fine on PC, Very Poor on Quest. One platform passing says nothing about the other.
    _build_performance_fixture(triangles=20001, used_materials=5, bones=120, basic_meshes=3)
    report = audit_scene()
    perf = report["performance"]["platforms"]
    assert perf["pc"]["estimated_rank"] == "Good" and perf["pc"]["meets_target"], perf["pc"]
    assert perf["quest"]["estimated_rank"] == "Very Poor" and not perf["quest"]["meets_target"], perf["quest"]
    found = [i for i in report["issues"] if i["code"] == "performance_below_target"]
    assert [i["platform"] for i in found] == ["quest"] and report["status"] == "warn", found
    assert all(s in found[0]["message"] for s in ("triangles 20001", "material_slots 5", "basic_meshes 3")), found
    print("  [ok] 20,001-triangle fixture: PC Good on target, Quest Very Poor reported separately")

    for bad in ({"platforms": ["switch"]}, {"targets": {"pc": "Great"}}):
        try:
            audit_scene(**bad)
            raise AssertionError(f"accepted {bad}")
        except ValueError:
            pass
    print("  [ok] unknown platform or rank is refused")

    # Zero-area triangles: collapse one triangle of a 10-triangle mesh by moving a corner onto another.
    _build_performance_fixture(triangles=10, used_materials=1, bones=1)
    body = bpy.data.objects["BudgetBody"].data
    body.vertices[1].co = body.vertices[0].co
    body.update()
    report = audit_scene()
    row = report["meshes"][0]
    notes = [i for i in report["issues"] if i["code"] == "zero_area_triangles"]
    assert (row["triangles"], row["zero_area_triangles"]) == (10, 1), row
    assert (report["triangle_count"], report["zero_area_triangle_count"]) == (10, 1), report["triangle_count"]
    assert report["performance"]["measured"]["triangles"] == 9, report["performance"]["measured"]
    assert report["performance"]["zero_area_triangles_excluded"] == 1, report["performance"]
    assert len(notes) == 1 and notes[0]["severity"] == "info" and "count 9" in notes[0]["message"], notes
    assert minimal["zero_area_triangle_count"] == 0 and minimal["meshes"][0]["zero_area_triangles"] == 0, minimal
    print("  [ok] zero-area triangle: counted, noted as info, excluded from the rank estimate (10 authored, 9 estimated)")
    print("SELF-TEST PASS: fixture scenes audited without modifying geometry, transforms or bone names")
    print(json.dumps(minimal, indent=2))


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json-out", type=Path, help="Write report JSON to this path")
    parser.add_argument("--bone-table", type=Path, default=DEFAULT_BONE_TABLE,
                        help="Humanoid bone-name table JSON (default: data/humanoid_bones_v1.json)")
    parser.add_argument("--budgets", type=Path, default=DEFAULT_BUDGETS,
                        help="Performance budget table JSON (default: data/performance_budgets_v1.json)")
    parser.add_argument("--platforms", help="Comma-separated platforms to judge, e.g. 'pc' (default: all in the table)")
    parser.add_argument("--target-rank", action="append", default=[], metavar="PLATFORM=RANK",
                        help="Override a platform's target rank, e.g. --target-rank quest=Good (repeatable)")
    parser.add_argument("--self-test", action="store_true", help="Build and audit in-memory fixtures")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(args)


def main() -> int:
    args = _arguments()
    if args.self_test:
        # Blender exits 0 after an uncaught Python error, so turn a failed check into exit code 1 here.
        try:
            run_self_test()
        except Exception:
            traceback.print_exc()
            print("SELF-TEST FAILED")
            return 1
        return 0
    try:
        if any("=" not in item for item in args.target_rank):
            raise ValueError("--target-rank needs PLATFORM=RANK, e.g. quest=Good")
        targets = dict(item.split("=", 1) for item in args.target_rank)
        platforms = [p.strip() for p in args.platforms.split(",") if p.strip()] if args.platforms else None
        report = audit_scene(args.bone_table, args.budgets, platforms, targets)
    except ValueError as error:
        print(f"AUDIT STOPPED: {error}")
        return 1
    payload = json.dumps(report, indent=2)
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(payload + "\n", encoding="utf-8")
    print(payload)
    return 1 if report["status"] == "fail" else 0


if __name__ == "__main__":
    raise SystemExit(main())
