# Descriptor setup (the avatar prefab)

The descriptor setup is step 4 of the [Avatar Pipeline](AVATAR_PIPELINE.md#the-avatar-pipeline). VRChat needs an avatar root that carries a **VRC Avatar Descriptor**. This tool creates one as a separate prefab beside the model, so the model file and its import settings stay untouched.

- Tool: `Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/AvatarDescriptorSetup.cs`
- Self-test: `AvatarDescriptorSetupSelfTest.cs`, part of `frosty.ps1 selftest`

## What it does

It creates `<model folder>/<model name>_Avatar.prefab`: the imported model, still linked to it so later model changes carry through, plus a `VRCAvatarDescriptor` and the `PipelineManager` the SDK requires. The VRoid set-up uses the same tool on the prefab UniVRM makes.

**View position.** This is where the wearer's eyes are in VRChat. It is set to:

- the midpoint of the eye bones, when both are mapped; otherwise
- an estimate from the part of the mesh skinned mainly to the head bone: centred, 62% of the way up that part, at its front surface.

The report says which method was used.

**Lip sync.** When one skinned mesh has all 15 of VRChat's viseme blend shapes, named `vrc.v_sil` to `vrc.v_ou` or `v_sil` to `v_ou` (in any case, with `vrc.v_ee` or `v_ee` also accepted for E), lip sync is set to **Viseme Blend Shape** with that mesh and those shapes. If some are missing, nothing is wired, and the report's `lip_sync` section names the missing ones. VRoid avatars use jaw-flap lip sync instead (see [VRoid files](AVATAR_GUIDE.md#vroid-files)).

Everything else on the descriptor stays at the SDK's defaults: playable layers, expressions menu and parameters, and eye look. The avatar ID on the Pipeline Manager stays empty until the first upload.

It creates nothing unless `-descriptorApply` is given. An existing prefab is never overwritten; the report's `mode` is then `nothing_to_do`. A model that is not imported as humanoid is refused. Run the [setup helper](AVATAR_SETUP_HELPER.md) first.

## Running it directly

Close the Unity Editor, then from the toolkit folder:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'

# Dry run: reports the view position and where the prefab would go
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarDescriptorSetup.Run -descriptorAsset Assets/FrostyNeon/Avatars/FrostyNeonBot.fbx -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\descriptor.log

# Create the prefab: add -descriptorApply. Another location: -descriptorPrefab Assets/.../Name_Avatar.prefab

# Self-test
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarDescriptorSetupSelfTest.Run -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\descriptor_selftest.log
```

The report goes to `Logs/FrostyNeon/descriptor_<asset name>.json`, or the path given with `-descriptorOut`. It holds the mode, the prefab path, whether the prefab existed or was created, the view position and how it was found, the lip sync result, and the check of the prefab (or of the model, in a dry run). The exit code is 0 when that check is `pass` or `warn`, and 1 when it is `fail` or the tool could not run.

## Limits

- The view position estimate is a rule of thumb. On a head with big hair, a hat, ears or a long neck, it can be off. Look at the eye mark in the preview pictures, and if it is wrong, correct the view position on the prefab in Unity.
- Expressions, playable layers, eye look and blinking are not set up.
- To redo the prefab, for example after changing the avatar's height, delete the prefab and its `.meta` file and run the tool (or Prepare) again.

## Tested

- The self-test covers a dry run, creating the prefab, leaving an edited prefab alone, refusing a non-humanoid model, and lip sync on meshes with the full viseme set, the `v_` spelling, and two shapes missing.
- FrostyNeonBot: the estimated view position sits at the robot's eye panels, checked against measurements in Blender. Its prefab has since been uploaded and worn in VRChat.
- On the four free test avatars that have all 15 visemes, lip sync was wired by this tool.
