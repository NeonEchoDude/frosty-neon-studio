# Setup helper (import settings)

The setup helper is step 2 of the [Avatar Pipeline](AVATAR_PIPELINE.md#the-avatar-pipeline). A model imported into Unity with the default settings fails VRChat's upload checks: mesh Read/Write is off, and the rig is not humanoid, or is humanoid with the Chest bone unmapped. The helper changes the import settings that fix this, lists every change, and runs the [preflight](AVATAR_PREFLIGHT.md) on the result.

- Tool: `Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/AvatarSetupHelper.cs`
- Self-test: `AvatarSetupHelperSelfTest.cs`, part of `frosty.ps1 selftest`

You normally run it through `frosty.ps1 model`, `prepare` or `all`, or the Pipeline window's **Prepare**.

## What it changes

Only import settings, which Unity keeps in the model's `.meta` file. The model file itself is never modified.

| Setting | Change | When |
|---|---|---|
| Animation Type | Humanoid | If it is anything else |
| Avatar Definition | Create From This Model | If it is anything else |
| Convert Units and Scale Factor | Convert Units off, with the file's unit scale (for example 0.01) multiplied into Scale Factor, so the size stays the same | If Convert Units is on and the file is not in metres. With Convert Units on, Unity stores the humanoid skeleton unconverted (100 times too large for centimetres), and VRChat uses that skeleton as the rest pose. |
| Humanoid skeleton | Rebuilt from the model | If the stored skeleton still differs in size from the model after the settings above |
| Scale Factor | Scaled so the model is the given height | With `-setupHeight <metres>` (`-Height` in `frosty.ps1`) |
| Scale Factor | Multiplied by 100 | Without a height, if the model is under 0.3 m tall and would be 0.5 to 3 m after multiplying by 100 (units read as centimetres) |
| Mesh Read/Write | On | If off |
| Legacy Blendshape Normals | On | If blend shape normals are set to Calculate (the SDK refuses to upload otherwise) |
| Humanoid Chest bone | The chest bone | Only if Unity left the Chest slot empty and exactly one bone fits |

The Chest candidate is the bone that is the direct parent of the neck and of both shoulders, provided it is not already used for another slot and lies below the Spine bone. This is the arrangement the SDK requires. If the neck and shoulders have different parents, or their parent is already the Spine, nothing is written, and the report says why (`chest_not_assigned`). All other slots keep Unity's automatic mapping.

It does not add an avatar descriptor or materials, and does not build or upload.

## Modes

It never changes your model unless `-setupApply` is given.

| Mode | Options | Effect |
|---|---|---|
| Dry run (default) | `-setupAsset <model>` | Tries the changes on a temporary copy, reports what would change and the predicted check result, and deletes the copy. |
| Apply to a copy | `-setupAsset <model> -setupApply -setupCopyTo Assets/.../Name.fbx` | Copies the model with its settings to the new path and applies the changes there. Refuses if the target exists. |
| Apply in place | `-setupAsset <model> -setupApply` | Changes the model's own import settings. |

To run it directly, close the Unity Editor, then from the toolkit folder:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'

# Dry run
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarSetupHelper.Run -setupAsset Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1.fbx -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\setup.log

# Self-test
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarSetupHelperSelfTest.Run -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\setup_selftest.log
```

`-setupOut <file.json>` sets the report path. The default is `Logs/FrostyNeon/setup_<asset name>.json` in the project. The exit code is 0 when the helper finished and the check afterwards is `pass` or `warn`, and 1 when the check fails, a problem was reported, or the helper could not run; the log says why. There is no menu item, so the helper cannot be triggered by accident.

## The report

| Field | Meaning |
|---|---|
| `mode` | `dry_run`, `applied_to_copy` or `applied_in_place` |
| `source_asset`, `target_asset` | The model given, and the model whose settings were changed (`null` for a dry run) |
| `settings_changed` | `false` for a dry run |
| `changes_made` / `changes_that_would_be_made` | Each change as `setting`, `before`, `after` |
| `problems` | What the helper could not do: `humanoid_avatar_invalid`, `chest_not_assigned`, `chest_assignment_rejected` |
| `preflight_before` | Status and finding codes of the model as given |
| `preflight_after` | The full check afterwards (a prediction, in a dry run) |
| `status` | `fail` if there is a problem or the check afterwards fails; otherwise the check's status |

The helper does not change materials. A model whose materials use Unity's Standard shader therefore still fails for Quest afterwards (`sdk_quest_unsupported_shader`), so the overall status is `fail` and the exit code is 1 even when everything the helper handles is fixed. Read `preflight_after.platforms.pc.status` for the PC result. The [material setup](AVATAR_MATERIAL_SETUP.md), the next pipeline step, handles shaders.

## Limits

- It fixes only what is listed above. Other findings, such as performance rank, blend shapes switched on, missing materials or limb child order, need fixing in the source file or by hand.
- A skeleton Unity cannot map as humanoid is reported, not guessed at. Unity's own messages about it go to the log, not the report.
- Applying in place rewrites the model's `.meta` file. Keep a copy, or use the copy mode, if you want the previous settings back.
- A dry run briefly creates and deletes `Assets/FrostyNeon/_SetupDryRun` in the project.
- Passing the check is not the same as passing the VRChat SDK's upload. See [Avatar preflight](AVATAR_PREFLIGHT.md#limits).

## Tested

- The self-test covers dry runs, applying to a copy and in place, a model first set to Generic, a rig that cannot be humanoid, refusing an existing copy target or a file that is not a model, and the Chest rule on bones built in memory (a shared parent chosen; shoulders under different parents, or a parent already used as Spine, not chosen). The test models are unchanged afterwards.
- FrostyNeonBot: Animation Type, mesh Read/Write and the Chest slot changed; the model file kept the same hash.
- Six free avatars in centimetres, with Mixamo bone names, were fixed for Convert Units and size. Before the Convert Units rule existed, two of them uploaded but were broken in VRChat; after it, they work. See [Tested with](TEST_AVATARS.md).
