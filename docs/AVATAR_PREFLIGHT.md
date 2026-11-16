# Avatar preflight (the check)

The preflight is the check behind every **Check** in the toolkit: the app's avatar panel, the Avatar Guide's step 2, `frosty.ps1 inspect`, `check` and `prepare`, and the uploader. For a model or avatar prefab in the Unity project, it reports what the VRChat SDK measures for PC and for Quest, and the problems the SDK's upload would raise about the rig, size, meshes, textures and shaders.

It only reads. It changes no asset or import setting, saves no scene, and builds and uploads nothing. It works on a temporary copy in a preview scene, so the scene you have open is not touched.

- Tool: `Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/AvatarPreflight.cs`
- Self-test: `AvatarPreflightSelfTest.cs`, part of `frosty.ps1 selftest`

## Running it directly

Most people never need to. The app, the Guide and `frosty.ps1` run it for you. To run it on its own, close the Unity Editor, then from the toolkit folder:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'

# Check one model or prefab
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarPreflight.Run -preflightAsset Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\preflight.log

# Self-test
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarPreflightSelfTest.Run -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\preflight_selftest.log
```

| Option | Meaning |
|---|---|
| `-preflightAsset <Assets/...>` | Required. A model or prefab already in the project. |
| `-preflightOut <file.json>` | Report path. Default: `Logs/FrostyNeon/preflight_<asset name>.json` in the project. |
| `-preflightTargetPc <rank>` | Target performance rank for PC. Default `Good`. |
| `-preflightTargetQuest <rank>` | Target performance rank for Quest. Default `Medium`. |

Ranks are `Excellent`, `Good`, `Medium`, `Poor` and `VeryPoor`. The exit code is 0 when the status is `pass` or `warn`, and 1 when it is `fail` or the check could not run. If the log contains `error CS`, a script did not compile and the result cannot be trusted.

With the Editor open, the same check is on the menu: select a model in the Project window, then choose **Frosty Neon > Preflight Selected Avatar Model**. The self-test is **Frosty Neon > Developer > Run Avatar Preflight Self-Test**. Only the batch form has been tested.

## The report

| Field | Meaning |
|---|---|
| `schema_version` | Report format version (currently 4) |
| `unity_version`, `vrchat_sdk_avatars_version`, `active_build_target` | What produced the numbers |
| `asset`, `import_settings` | The model and how Unity imported it: animation type, scale settings, mesh Read/Write |
| `renderers` | Each renderer: type, mesh, triangles, material slots, bones, blend shapes, and per material slot the material, shader, whether it is embedded in the model, and main texture |
| `platforms.pc`, `platforms.quest` | See below |
| `rig` | Whether there is an Animator, whether it is humanoid, Unity's bone mapping, shoulder height, and the stored skeleton's scale |
| `avatar_descriptor` | On an avatar prefab: whether the VRC Avatar Descriptor and Pipeline Manager are present, whether an avatar ID is set, the view position and the lip sync mode. Reported, not judged. |
| `findings` | Each finding's `severity`, `code`, `source`, `message`, and where relevant `platform` and `objects` |
| `quest_shader_whitelist` | The shaders the installed SDK allows on Quest |
| `status` | `fail` if any finding is an error, `warn` if any is a warning, otherwise `pass` |
| `limitations` | What the report does not cover |

Each platform section holds the VRChat SDK's own results, calculated with the PC limits or with the mobile limits:

| Field | Meaning |
|---|---|
| `sdk_overall_rank` | The SDK's overall performance rank |
| `sdk_category_ranks` | The SDK's rank for each category it rates |
| `sdk_statistics` | The SDK's raw values: triangles, meshes, material slots, bones, bounds, texture memory and component counts |
| `target_rank`, `meets_target`, `categories_over_target` | Comparison with the target rank |
| `status` | `pass`, `warn` or `fail` for this platform, ignoring findings that belong only to the other platform |

PC and Quest are judged separately. An avatar can meet its PC target and miss its Quest target. When you upload, only the status of the platform you upload for counts.

## Findings

Each finding's `source` says where it comes from:

- `vrchat_sdk_performance`: the SDK's performance rank compared with the target.
- `vrchat_sdk_condition`: a condition the SDK's upload checks, re-evaluated by the preflight with the SDK's own message text. These checks are private parts of the SDK's build panel, so the preflight repeats their conditions rather than calling them.
- `toolkit`: the toolkit's own checks.

| Code | Level | Source | Meaning |
|---|---|---|---|
| `performance_below_target` | warning | SDK performance | A platform's rank is worse than its target. Lists the categories over the target. |
| `mesh_read_write_disabled` | error | SDK condition | Meshes were imported with Read/Write off. The SDK requires it on. |
| `sdk_legacy_blend_shape_normals` | error | SDK condition | Blend shape normals are set to Calculate without **Legacy Blendshape Normals**. The SDK refuses to upload, even if the mesh has no blend shapes. |
| `sdk_viseme_face_mesh_missing` | error | SDK condition | Lip sync is set to Viseme Blend Shape, but no face mesh is assigned. |
| `sdk_texture_streaming_mipmaps` | error | SDK condition | A texture has mipmaps but not **Streaming Mip Maps**. The SDK refuses to upload. |
| `sdk_texture_too_large` | error | SDK condition | A texture is larger than the SDK's limit of 8192 pixels. |
| `sdk_quest_unsupported_shader` | error, Quest only | SDK condition | A material uses a shader that is not on the SDK's `VRChat/Mobile` list. `objects` lists the shaders. Does not affect PC. |
| `sdk_no_animator` | warning | SDK condition | No Animator on the root, so it will not animate in VRChat. Usual for a model imported as Generic. |
| `sdk_not_humanoid` | warning | SDK condition | There is an Animator, but it is not humanoid. |
| `sdk_feet_not_specified`, `sdk_upper_arms_not_specified` | error | SDK condition | Humanoid, but those bones are not mapped. |
| `sdk_feet_below_origin` | warning | SDK condition | The feet are below the model's origin. |
| `sdk_too_short` | error | SDK condition | Shoulder height under 0.2 m. |
| `sdk_shorter_than_average`, `sdk_taller_than_average`, `sdk_too_tall` | warning | SDK condition | Shoulder height under 1 m, over 2.5 m, or over 5 m. |
| `sdk_head_hands_feet_unmapped` | error | SDK condition | The head, a hand or a foot is not mapped. |
| `sdk_spine_missing_elements` | error | SDK condition | The neck, a shoulder, hips, spine or chest is not mapped. `objects` lists which. |
| `sdk_spine_hierarchy_incorrect` | error | SDK condition | The shoulders and neck are not direct children of the Chest (or of the UpperChest, if one is mapped). |
| `sdk_arm_first_child_order`, `sdk_leg_first_child_order` | warning | SDK condition | A lower arm or leg bone is not the first child of the upper one. |
| `sdk_split_hierarchy` | warning | SDK condition | Hips is not an ancestor of both hands and both feet. |
| `blend_shape_default_weight_nonzero` | warning | toolkit | A blend shape is switched on by default, for example a Blender shape key exported at a non-zero value. |
| `material_shader_missing` | error | toolkit | A material's shader is not in the project, so it shows pink. Usual for Booth avatars. Names the package when it can tell: lilToon, Poiyomi Toon Shader or Unity-Chan Toon Shader. |
| `humanoid_rest_pose_scale_mismatch` | error | toolkit | Unity's stored humanoid skeleton, which VRChat uses as the rest pose, is not the same size as the model. `rig.stored_skeleton_scale` gives the factor. Seen as 100 times too large on centimetre FBX files with Convert Units on; such avatars upload, but are broken in VRChat. Prepare fixes it. |
| `model_very_small` | warning | toolkit | The model is under 0.3 m tall, usually because of the wrong units. Prepare with `-Height`. |
| `non_unit_scale` | warning | toolkit | An object or bone has a scale other than 1. |
| `missing_material`, `renderer_without_mesh` | warning | toolkit | An empty material slot, or a renderer with no mesh. |
| `no_renderers` | error | toolkit | Nothing to render. |
| `sdk_version_differs` | warning | toolkit | The installed SDK is not 3.10.6, so the SDK-condition checks may be out of date. |

As in the SDK, the rig check stops at its first error. For example, if Chest is not mapped, the hierarchy and limb-order conditions are not checked until it is.

The plain-language text that the app and the Guide show for each code is in `FindingText.cs`.

## Limits

- **It is not an SDK build.** Of the SDK's upload checks, it covers the rig, size, mesh Read/Write, blend shape normals, texture streaming and size, the viseme face mesh and the Quest shader list. Other components, descriptor settings, upload size and the rest are not covered. A `pass` here does not guarantee the SDK accepts the avatar.
- **The SDK-condition checks follow SDK 3.10.6.** After an SDK update, compare them with the SDK's `VRCSdkControlPanelAvatarBuilder.cs` (in `Packages/com.vrchat.avatars/Editor/VRCSDK/SDK3A/`) and adjust.
- **Quest ranks** apply the SDK's mobile limits to the assets as imported for the current build target. Download size is not checked.
- **The Quest shader check runs on every build target.** The SDK's build panel runs it only for Android and iOS, so a PC-only avatar also shows the Quest finding. Read the PC `status` for a PC-only avatar. The SDK has no shader list for PC, and the preflight does not judge PC shaders.
- **A bare model** has no PhysBones, contacts, constraints, particles or audio, so those categories read Excellent. They say nothing about the finished avatar.
- **It fixes nothing.** The Fix, the setup helper and the material setup do that.

## Relation to the Blender audit

The [Blender audit](PERFORMANCE_BUDGETS.md) estimates before export; the preflight measures after import, with the SDK's own code. Where both report the same thing (triangles, mesh counts, material slots, bones, rank limits), they agreed on the test models. The preflight adds what only Unity knows: Unity's bone mapping, the import settings, and the SDK's values for bounds, texture memory and components.

## Tested

- The self-test covers generated test models with and without a humanoid rig, with and without the Chest bone mapped, with Read/Write on and off, with blend shapes switched on and non-unit scale; objects with too many triangles, meshes and material slots for Quest; the Quest shader list; custom target ranks; and refusal of unknown ranks and missing assets. Test models are unchanged afterwards.
- On FrostyNeonBot, the result matches the SDK's own numbers: PC Excellent, Quest Medium, 3,552 triangles. It was run on both the Windows and Android build targets with identical results.
- Every finding that stopped a real upload during testing (`sdk_legacy_blend_shape_normals`, `sdk_texture_streaming_mipmaps`) and every problem found in VRChat (`humanoid_rest_pose_scale_mismatch`) is now a finding. See [Tested with](TEST_AVATARS.md).
