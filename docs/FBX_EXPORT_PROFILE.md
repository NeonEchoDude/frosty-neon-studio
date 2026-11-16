# Blender FBX export profile (`unity_avatar_fbx` v1)

One written-down, testable set of FBX settings for sending a skinned avatar from Blender to Unity, so that axis and scale are decisions on record rather than whatever Blender's export dialog last held.

`frosty.ps1 export` and `frosty.ps1 all` use this profile (see [Commands](AVATAR_PIPELINE.md#blender-files)). This page is for running the exporter directly and for understanding its choices.

- Profile: `tools/blender/profiles/unity_avatar_fbx_v1.json`
- Tool: `tools/blender/export_avatar_fbx.py`
- Verified in: Blender 5.2.3 LTS and Unity 2022.3.22f1 (import of the test figure; see [Measured in Unity](UNITY_VALIDATION.md))

## Commands

Run from the toolkit folder in PowerShell:

```powershell
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'

# Test the profile against a generated fixture (writes only to a temp folder)
& $blender --background --factory-startup --python tools/blender/export_avatar_fbx.py -- --self-test

# See what would happen for a real file; writes nothing
& $blender --background path\to\avatar.blend --python tools/blender/export_avatar_fbx.py -- --dry-run

# Export a generated test figure (used for the Unity checks): 'basic' (4 bones) or 'humanoid' (21 bones, T-pose)
& $blender --background --factory-startup --python tools/blender/export_avatar_fbx.py -- --fixture basic --out fixture.fbx
& $blender --background --factory-startup --python tools/blender/export_avatar_fbx.py -- --fixture humanoid --out humanoid.fbx --fixture-blend-out humanoid.blend

# Export, re-import to compare, and keep the report
& $blender --background path\to\avatar.blend --python tools/blender/export_avatar_fbx.py -- --out Assets\Avatars\Exports\PC\avatar.fbx --roundtrip --json-out Assets\Avatars\Exports\PC\avatar.report.json
```

Exit code 0 means pass or warnings only; 1 means the export was stopped or a check failed. An existing output file is not replaced unless `--overwrite` is given. The `.blend` is never saved.

The same FBX serves PC and Quest; the profile has no platform-specific settings. Platform differences, such as performance limits and shaders, are handled in Unity by the check and the Quest version.

## Axis and scale choices

| Setting | Value | Why |
|---|---|---|
| `axis_up` / `axis_forward` | `Y` / `-Z` | Writes a Y-up file, matching Unity's up axis. A character modelled facing Blender's front (-Y) is expected to face Unity's forward (+Z). |
| `use_space_transform` | `true` | Lets the exporter record the axis conversion. |
| `bake_space_transform` | `false` | Blender labels this option experimental. With it off, the axis conversion is stored as a -90° X rotation on each top-level object and vertex data is left in Blender's own axes. |
| `global_scale` | `1.0` | No extra scaling. |
| `apply_unit_scale` | `true` | The scene's unit scale is carried into the file. |
| `apply_scale_options` | `FBX_SCALE_ALL` | Unit scaling goes into the file header (`UnitScaleFactor = 100`, i.e. 1 unit = 1 m) instead of onto each object, so objects keep scale 1 rather than 100. |

Other choices: only `ARMATURE` and `MESH` objects are exported; modifiers are **not** applied (`use_mesh_modifiers: false`) so the file holds the mesh as authored and shape keys are kept; leaf bones are not added; animation is not baked; textures are not embedded. The remaining options are written in the profile at Blender 5.2.3's default values.

The tool never applies, rotates or rescales anything in the source. Unapplied object scale or rotation, unapplied modifiers and shape keys with a non-zero value are reported as warnings and exported as they are.

## What the tool checks

1. **Profile coverage.** Every option of Blender's FBX exporter and importer must be present in the profile. A Blender update that adds or renames an option stops the tool until the profile is updated.
2. **Source untouched.** Object transforms, mesh vertices, modifiers, selection, visibility and the active object are compared before and after the export.
3. **File header.** The written FBX is parsed and its axis/unit header compared with `expected_fbx_global_settings`.
4. **Round trip (`--roundtrip`).** The FBX is imported into an empty Blender session and compared with the source in world space: vertex positions, triangle count, shape key names and offsets, vertex groups, materials, bone names, parents and head positions. Tolerance is 0.0001 m. Bone tail positions are reported but not enforced, because FBX stores joints, not Blender bone tails.

### Fixture

`--self-test` builds a small skinned figure in memory: a 0.4 × 0.2 × 1.6 m body standing on the origin, a "nose" vertex towards -Y, a "left hand" vertex towards +X, four bones, four vertex groups, one material and one shape key (`vrc.v_aa`). It is different along every axis so that a swapped or mirrored axis cannot pass. No binary fixture is stored in Git.

The self-test also proves the checks can fail: an incomplete profile is rejected, a Z-up export is caught by the header check, a file written at 2× scale is caught by the round trip, and an existing output file is not overwritten.

### Measured results (Blender 5.2.3)

- Header: `UpAxis=1 (+Y)`, `FrontAxis=2 (+Z)`, `CoordAxis=0 (+X)`, `UnitScaleFactor=100`.
- In the file, `Armature` and `Body` each have local rotation (-90, 0, 0) and local scale (1, 1, 1).
- Round trip: largest vertex error 2.6e-7 m, largest bone head error 2.3e-7 m, shape key error 8.3e-9 m. Bone tails moved by up to 0.091 m (reported, expected).

## Checked in Unity

The test figure was imported into the Creator Companion project in Unity 2022.3.22f1 with default import settings and measured by an Editor script. Every prediction held. The full numbers and the procedure are in [Measured in Unity](UNITY_VALIDATION.md). In short:

- Scale is 1:1; no object carries a scale of 100. File Scale 1, Convert Units on, Bake Axis Conversion off.
- The root object has no rotation; `Armature` and the mesh object each keep about -90 degrees on X, which is the unbaked axis conversion.
- Blender (x, y, z) appears in Unity at (-x, z, -y): up stays up, Blender's front (-Y) is Unity's forward (+Z), and left stays left. The figure is not mirrored.
- The blend shape arrives with the right name and direction.

One thing the Blender round trip cannot show: a shape key's current value is exported as the blend shape's default weight, so a key left at 1.0 arrives switched on in Unity. The exporter warns about it (`shape_key_value_nonzero`) and reports each blend shape's default weight.

If a later Unity or Blender version changes any of this, change the profile (new `profile_version`), re-run the self-test, and repeat the Unity check. Do not compensate with manual edits in Unity.

Humanoid mapping was checked separately with the 21-bone test rig (`--fixture humanoid`); see [Measured in Unity](UNITY_VALIDATION.md). FrostyNeonBot, exported with this profile, passes the VRChat SDK's upload checks and has been uploaded.
