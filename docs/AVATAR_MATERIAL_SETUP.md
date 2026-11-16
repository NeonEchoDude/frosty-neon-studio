# Material setup

The material setup is step 3 of the [Avatar Pipeline](AVATAR_PIPELINE.md#the-avatar-pipeline). A model imported with Unity's defaults gets materials generated inside the import, using Unity's Standard shader. The VRChat SDK accepts that on PC and refuses it on Quest. This tool gives the model one set of materials that works on both, without touching the model file.

- Tool: `Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/AvatarMaterialSetup.cs`
- Self-test: `AvatarMaterialSetupSelfTest.cs`, part of `frosty.ps1 selftest`

This tool is for plain models. Ready-made avatars, such as Booth avatars, keep their creator's materials; for Quest, they get a separate [Quest version](AVATAR_GUIDE.md#quest-version) instead.

## What it does

1. For each material embedded in the model, it creates a material asset at `<model folder>/Materials/<material name>.mat`, using a shader from the SDK's Quest list. It copies the colour, the main texture with its tiling and offset, and the metallic and smoothness values where the new shader has them.
2. It points the model's import settings at those assets (a material remap stored in the `.meta` file).
3. It turns on Streaming Mip Maps for the mipmapped textures those materials use. The VRChat SDK refuses to upload without it.
4. It runs the [preflight](AVATAR_PREFLIGHT.md) and writes a report.

It changes nothing unless `-materialApply` is given. The default is a dry run that reports what it would create. An existing `<name>.mat` is reused as it is and never overwritten. Once all of a model's materials are remapped, running it again does nothing, and the report's `mode` is `nothing_to_do`. A shader that is not on the SDK's Quest list is refused.

The default shader is `VRChat/Mobile/Standard Lite`. It has a colour property, so it works for untextured models. `VRChat/Mobile/Toon Lit` and `VRChat/Mobile/Diffuse` take only a texture and would turn an untextured model white.

PC uses the same mobile materials as Quest. Separate PC and Quest materials are not supported by this tool.

## Running it directly

Close the Unity Editor, then from the toolkit folder:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'

# Dry run
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarMaterialSetup.Run -materialAsset Assets/FrostyNeon/Avatars/FrostyNeonBot.fbx -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\materials.log

# Apply: add -materialApply. Another shader from the Quest list: -materialShader "VRChat/Mobile/Toon Standard"

# Self-test
& $unity -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarMaterialSetupSelfTest.Run -logFile Unity\FrostyAvatarProject\Logs\FrostyNeon\material_selftest.log
```

The report goes to `Logs/FrostyNeon/materials_<asset name>.json`, or the path given with `-materialOut`. It lists each material with its previous shader, the asset path, the action taken and the colour, followed by the check result. The exit code is 0 when the check afterwards is `pass` or `warn`, and 1 when it is `fail` or the tool could not run. A dry run on a model that still uses Standard therefore exits with 1, because the Quest finding is still there.

## Limits

- Only the colour, main texture with tiling and offset, metallic and smoothness are carried over. Normal maps, emission, transparency and everything else on the original material are not.
- Standard Lite is not Standard: lighting and reflections differ. The values are copied, but the look is not guaranteed to match.
- To undo, remove the remaps from the model's import settings (or restore its `.meta` file from a copy) and delete the `Materials` folder.

## Tested

- The self-test covers a dry run, applying, applying again (nothing to do), reusing an existing asset, copying properties including a texture's tiling and offset, and refusing Standard as a target shader.
- FrostyNeonBot: two materials (`Frosty Cyan` and `Midnight Navy`) created with Standard Lite; the check then passed on PC (Excellent) and Quest (Medium); the model file was unchanged.
- Free avatars with one texture each kept their texture; the two whose preview pictures were checked by eye looked correct.
