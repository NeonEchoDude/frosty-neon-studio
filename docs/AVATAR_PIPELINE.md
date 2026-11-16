# Commands and the Avatar Pipeline

Everything Frosty Neon Studio and the Avatar Guide do is also a PowerShell command in `frosty.ps1`. This page lists the commands, then describes the **Avatar Pipeline**, the steps that turn a plain model into a VRChat avatar.

## Running `frosty.ps1`

Open PowerShell in the toolkit folder and run:

```powershell
.\frosty.ps1 <command> <target> [options]
```

If Windows refuses to run the script, use this form instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\frosty.ps1 <command> <target> [options]
```

`.\frosty.ps1 help` prints the list of commands.

Most commands start Unity hidden in batch mode, which takes about half a minute each time. **Close the Unity Editor first**: two copies of Unity cannot use one project, and the command stops with a message if the Editor has it open.

Each command prints a summary and writes a JSON report to `Unity\FrostyAvatarProject\Logs\FrostyNeon\`. A command exits with code 0 when it did what was asked, and 1 when it did not; the message says why.

Blender and Unity are found automatically: Unity by the version in the project's `ProjectSettings/ProjectVersion.txt`, in Unity Hub's default folder (`C:\Program Files\Unity\Hub\Editor`) or the install folder set in Unity Hub's preferences, and Blender under `C:\Program Files\Blender Foundation\`. To use other installs, set `FROSTY_UNITY` or `FROSTY_BLENDER` to the full path of `Unity.exe` or `blender.exe`.

## Commands

### Bringing an avatar in

| Command | What it does |
|---|---|
| `import <file.unitypackage>` | Lists the package's contents, imports it, then checks each avatar prefab it brought in (PC and Quest status, missing shaders). A package that contains scripts is refused unless you add `-AllowScripts`. `-DryRun` lists the contents without importing. |
| `vroid <file.vrm>` | Shows the creator's terms stored in the file, copies it into `Assets/FrostyNeon/VRoid`, and sets it up as `<name>_Avatar.prefab` with the eye position and lip sync. Needs UniVRM in the project and a VRM 0.0 file. If the terms prohibit changes, add `-AcceptTerms` only if you are the author or have permission. |
| `model <file.fbx>` | Copies a plain model into `Assets/FrostyNeon/Models/<name>/`, with its `<name>.fbm` or `Textures` folder if there is one, then runs `prepare` on the copy. Your file is not changed. `-Height 1.6` scales it to that many metres tall. If a different model with the same name is already there, it stops; `-Force` replaces it. |

### Checking and fixing an avatar prefab

These take the path of an avatar prefab inside the Unity project, such as `Assets/Shop/MyAvatar.prefab`. `list` shows the paths.

| Command | What it does |
|---|---|
| `list` | Lists the avatars in the project, with their kind and whether they have a Quest version. Changes nothing. |
| `inspect <prefab>` | Everything the app shows for one avatar: the check in plain words, the Fix plan, the Quest version, height, eye height, the creator's terms, the last upload and preview pictures. Changes nothing. |
| `fix <prefab>` | The Fix: turns on the import settings VRChat's upload requires (see [Avatar Guide](AVATAR_GUIDE.md#the-fix)). `-DryRun` lists the changes without making them; `-Undo` puts them back. |
| `quest <prefab>` | Makes a Quest version (see [Avatar Guide](AVATAR_GUIDE.md#quest-version)). `-Remove` deletes it and restores the texture settings. |

### Plain models and the pipeline

These take the path of a model inside the Unity project, such as `Assets/FrostyNeon/Models/MyAvatar/MyAvatar.fbx`.

| Command | What it does |
|---|---|
| `check <model>` | Runs the pipeline as a dry run: reports what each step would do. Changes nothing. |
| `prepare <model>` | Runs the pipeline: import settings, materials, avatar prefab, preview pictures and a final check. `-Height 1.6` scales the avatar to that many metres tall. |
| `preview <model or prefab>` | Saves front, three-quarter and back pictures to `Logs\FrostyNeon\previews`. |

### Uploading

| Command | What it does |
|---|---|
| `upload <model or prefab>` | Uploads to your VRChat account as a private avatar. Give the avatar prefab, or the model it was prepared from. `-Quest` uploads the Quest version, `-Name "My Avatar"` names a new avatar, and `-DryRun` checks without uploading. See [Uploading](AVATAR_UPLOAD.md). |

### Blender files

These need Blender. See [Blender export profile](FBX_EXPORT_PROFILE.md) and the [Blender scene audit](HUMANOID_BONE_REPORT.md).

| Command | What it does |
|---|---|
| `audit <file.blend>` | Checks a Blender avatar: bones, performance estimate, scene problems. Writes `<name>_audit_report.json` beside the file. Changes nothing. |
| `export <file.blend>` | Exports to FBX with the tested profile, into `Assets\Avatars\Exports\PC\`, and copies it into the Unity project as `Assets/FrostyNeon/Avatars/<name>.fbx`. Stops if the file exists; `-Force` replaces it. |
| `all <file.blend>` | `audit`, `export`, then `prepare`. `-Height` works here too. |

### PC and toolkit checks

| Command | What it does |
|---|---|
| `doctor` | Checks what this PC has. Changes nothing. See [below](#doctor). |
| `selftest` | Runs every self-test: two Blender, eight Unity, and the app's. Needs Blender and Unity, and takes a few minutes. |

### Doctor

`.\frosty.ps1 doctor` checks, in order:

1. VRChat Creator Companion.
2. The Unity version the project uses (2022.3.22f1).
3. Quest support: Unity's Android Build Support with its SDK, NDK and OpenJDK.
4. The VRChat Avatars SDK in the project (tested with 3.10.6).
5. The lilToon and Poiyomi shaders in the project (only needed if your avatar uses them).
6. UniVRM (only needed for VRoid files).
7. Blender (only needed for `.blend` files).
8. Free disk space (at least 10 GB recommended), and whether the Unity Editor has the project open.

Each line is marked **OK**, **MISSING** (install it first), **OPTIONAL** (needed for some things, such as Quest) or **INFO**, with what to do. It installs nothing. The result is also saved as `doctor.json` for the app.

## The Avatar Pipeline

The pipeline turns a plain model, such as an `.fbx` from Blender, Mixamo or a 3D store, into a VRChat avatar. `model`, `prepare` and `all` run it, and so does the **Prepare** button in the Avatar Pipeline window.

Steps, in order:

1. **Check the model as imported** ([Avatar preflight](AVATAR_PREFLIGHT.md)).
2. **Import settings** ([Setup helper](AVATAR_SETUP_HELPER.md)): humanoid rig, the Chest bone if Unity left it empty, mesh Read/Write, Legacy Blendshape Normals, units and scale.
3. **Materials** ([Material setup](AVATAR_MATERIAL_SETUP.md)): one set of materials using VRChat's mobile shader, which works on both PC and Quest, and Streaming Mip Maps on their textures.
4. **Avatar prefab** ([Descriptor setup](AVATAR_DESCRIPTOR_SETUP.md)): `<model>_Avatar.prefab`, with the VRChat avatar descriptor, the eye position and, if the model has all 15 viseme shapes, lip sync. In a dry run on a model that is not humanoid yet, this step is skipped with a note.
5. **Preview pictures.**
6. **Final check**, of the avatar prefab when there is one.

The model file is never modified. Running Prepare a second time changes nothing. Existing materials and prefabs are kept as they are.

**Good to know:**

- Because the pipeline uses VRChat's mobile shader for both versions, a model set up this way looks the same on PC and Quest, and simpler than with a PC shader. Normal maps, emission and transparency are not carried over.
- `-Height` is for models in unusual units. Use it when the check warns `model_very_small` or `sdk_too_short`. To change the size of an avatar that is already prepared, delete its `_Avatar.prefab` first, because the eye position is set when the prefab is created.
- The full report is `Logs\FrostyNeon\pipeline_<model>.json`.

### The Avatar Pipeline window

In Unity, **Frosty Neon > Avatar Pipeline** opens the same pipeline as a window, for people who know Unity. New to Unity? Use the [Avatar Guide](AVATAR_GUIDE.md) instead.

1. Drag the imported model (the `.fbx` in the Project window) into **Model**.
2. Optionally, enter **Height (m, optional)**.
3. Click **Check** for a dry run, or **Prepare** to apply. Prepare asks first.
4. When the avatar prefab exists, enter an **Avatar name** and click **Upload PC** or **Upload Quest**.

The window shows the summary, the status and the preview pictures.

## Tested

- A Mixamo-named test rig with fingers went from failing to passing on PC and Quest with Prepare, with all 39 bones mapped. A second Prepare changed nothing.
- Six free avatars from the 100Avatars collection were prepared with `-Height` and passed on PC and Quest, two of them with notes. See [Tested with](TEST_AVATARS.md).
- `check` on FrostyNeonBot passed without changing its files.
- The window's **Prepare** and **Upload PC** buttons have been used for a real upload.
- Not yet tested: the window's **Check** and **Upload Quest** buttons themselves, and `all` from start to finish on a real Blender avatar.
