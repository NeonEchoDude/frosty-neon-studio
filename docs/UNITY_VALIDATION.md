# Measured in Unity

What has been measured in the Unity Editor, as opposed to predicted from Blender. These measurements are the basis for the FBX export profile, the bone report, the performance budgets and the check. Repeat them when Unity, the VRChat SDK or Blender changes version.

## Environment

| Item | Value |
|---|---|
| Unity Editor | 2022.3.22f1, the version VRChat documents |
| VRChat SDK | Base 3.10.6, Avatars 3.10.6, installed by Creator Companion |
| Project | `Unity/FrostyAvatarProject`, created with Creator Companion |
| Community packages in the project | AudioLink, Av3Emulator, Gesture Manager, Av3Manager, EasyQuestSwitch and VRWorldToolkit, listed in `Packages/vpm-manifest.json`. Creator Companion installs them; they are not kept in Git, and the toolkit does not depend on them. |

## How the measurements are taken

`Assets/Editor/FrostyNeon/FbxImportProbe.cs` reads how Unity imported a test FBX, places a temporary copy in a new empty scene, measures it, and writes a JSON report. It saves nothing to the project. `SdkAvatarProbe.cs` calls the VRChat SDK's own performance calculation on the same test models.

1. Export the test figure from Blender with the profile:

   ```powershell
   & 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python tools/blender/export_avatar_fbx.py -- --fixture basic --overwrite --out Unity\FrostyAvatarProject\Assets\FrostyNeon\Fixtures\fbx_fixture_v1.fbx --roundtrip
   ```

   For the humanoid rig, use `--fixture humanoid` and `fbx_humanoid_fixture_v1.fbx`.

2. With the Unity Editor closed, run the probe:

   ```powershell
   & 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe' -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.FbxImportProbe.Run -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\probe.log
   ```

   `FbxImportProbe.RunHumanoid` and `FbxImportProbe.RunExplicitChest` run the two humanoid probes, and `SdkAvatarProbe.Run` the SDK probe. The same probes are on the **Frosty Neon > Developer** menu in the Editor (**Probe FBX Fixture Import**, **Probe Humanoid Fixture Mapping**, **Probe Humanoid Fixture With Explicit Chest**, **Probe Fixtures With VRChat SDK**). The menu versions open a new empty scene, so save your open scene first.

3. Read the report in `Unity/FrostyAvatarProject/Logs/FrostyNeon/`, for example `fbx_import_probe.json`.

## Blender to Unity: axis and scale

The basic test figure, exported with the [FBX profile](FBX_EXPORT_PROFILE.md) and imported with Unity's default settings:

| Check | Predicted | Measured in Unity |
|---|---|---|
| File Scale | 1 | 1 |
| Convert Units | on | on |
| Bake Axis Conversion | off | off |
| Root object rotation, scale | (0, 0, 0), (1, 1, 1) | (0, 0, 0), (1, 1, 1) |
| `Armature` rotation, scale | (-90, 0, 0), (1, 1, 1) | (-89.98, 0, 0), (1, 1, 1) |
| `Body` rotation, scale | (-90, 0, 0), (1, 1, 1) | (-89.98, 0, 0), (1, 1, 1) |
| Size, width × height × depth | 0.8 × 1.6 × 0.4 m | 0.8 × 1.6 × 0.4 m |
| Nose direction | Unity forward (+Z) | +Z, at (0, 1.5, 0.3) |
| Left-hand marker | | (-0.6, 1.3, 0) |
| `Hips` bone | height 0.9 m | (0, 0.9, 0) |
| Blend shape `vrc.v_aa` | present, default weight 0 | present, default weight 0, moves one vertex by (0, -0.02, +0.05) |

What this means:

- **Scale is 1:1.** One Blender metre is one Unity unit, and no object carries a scale of 100.
- **Axes.** A point at Blender (x, y, z) appears in Unity at (-x, z, -y). Blender's up (+Z) is Unity's up (+Y), and Blender's front (-Y) is Unity's forward (+Z).
- **Not mirrored.** The figure faces +Z in Unity and its left hand is at -X, which is its left side. Left stays left.
- **The -90° X rotation on `Armature` and the mesh is real.** It is the unbaked axis conversion the profile chose; the root object has none. Unity shows -89.98° where the file stores -90°; no measured vertex moved at five decimal places. VRChat's rig page says Blender exports should have a rest X rotation of 90 degrees; whether that refers to this is not established.
- **Shape key values become default blend shape weights.** A shape key left at 1.0 in Blender is exported as a default weight of 100 and arrives switched on in Unity. The exporter warns about this (`shape_key_value_nonzero`), and the check reports it (`blend_shape_default_weight_nonzero`).

## Humanoid mapping

**Unity's required bones.** `HumanTrait.RequiredBone` in Unity 2022.3.22f1 marks 15 of the 55 humanoid bones as required: Hips, Spine, Head, and on each side UpperArm, LowerArm, Hand, UpperLeg, LowerLeg and Foot. This is exactly the set in `tools/blender/data/humanoid_bones_v1.json`, and the audit self-test asserts it. Chest, Neck and the shoulders are optional for Unity, but VRChat requires them.

**Unity leaves Chest empty.** On the 21-bone test rig (Hips, Spine, Chest, Neck, Head, and on each side shoulder, upper arm, lower arm, hand, upper leg, lower leg, foot and toes, all named with Unity's slot names), Unity's automatic mapping filled 20 slots and left Chest empty, even though a bone named `Chest` is the parent of the neck and both shoulders. The same happened on FrostyNeonBot and on a Mixamo-named rig. The stored mapping (`Avatar.humanDescription`) agrees, and so does a call into Unity's internal auto-mapper, which is only indicative because the call is not an exact copy of the Configure window's button. Why Unity skips it has not been investigated.

**Writing Chest explicitly works.** With the bone written into the Chest slot in the import settings, Unity accepts it and the other 20 slots are unchanged. This is what the [setup helper](AVATAR_SETUP_HELPER.md) does when exactly one bone fits.

The 100Avatars test models, with Mixamo names, were mapped by Unity on their own, including UpperChest and 18 finger slots.

## VRChat SDK 3.10.6

**Rank limits.** The SDK ships its performance limits as assets in `Packages/com.vrchat.base`. All 40 numbers the Blender audit uses (five statistics, four ranks, PC and Quest) are identical to `tools/blender/data/performance_budgets_v1.json`.

**Counts.** The SDK's counts equal the Blender audit's on both test models:

| Test model | Triangles | Skinned meshes | Basic meshes | Material slots | Bones | Rank, PC / mobile |
|---|---|---|---|---|---|---|
| Basic figure | 14 | 1 | 0 | 1 | 4 | Excellent / Excellent |
| Humanoid rig | 255 | 1 | 0 | 1 | 21 | Excellent / Excellent |

On FrostyNeonBot, all counts and both ranks also agree, except triangles: Blender counts 3,696 and the SDK 3,552. The difference is exactly the mesh's 144 zero-area triangles, which the audit now leaves out. That Unity drops them is inferred from the exact match, not observed directly.

The SDK's bounding box is not simply the vertex extents: for the basic figure it is 0.80 × 1.60 × 0.45 m against 0.40 m of vertex depth. The audit does not estimate bounds.

**Rig rules**, as `VRCSdkControlPanelAvatarBuilder.AnalyzeIK` enforces them:

- Error if the head, both hands or both feet are not mapped.
- Error "Spine hierarchy missing elements" if the Neck, both Shoulders, Hips, Spine or Chest is not mapped.
- Error "Spine hierarchy incorrect" unless both shoulders and the neck are direct children of the UpperChest when one is mapped, and of the Chest otherwise. VRChat's Rig Requirements page names only Chest; the toolkit follows the SDK.
- Warning if the lower arm is not the first child of the upper arm, or the hand not the first child of the lower arm, and the same for legs.
- Warning if Hips is not an ancestor of both hands and both feet.

On the humanoid rig as Unity mapped it, these rules give the error "Spine hierarchy missing elements, please map: Chest". With Chest written explicitly, there are no errors or warnings. So the empty Chest slot would stop an upload.

**Import defaults.** Imported with Unity's default settings, every test model has mesh Read/Write off, which the SDK treats as an error before upload.

## Not yet measured

- A rig with an UpperChest bone from Blender. The SDK rule was read from its source; the 100Avatars models have UpperChest, but came from another pipeline.
- Android: the check has been run on the Android build target, and a Quest build of FrostyNeonBot has been uploaded, but nothing has been looked at on a Quest headset.
- How the mobile materials look compared with the original materials.
