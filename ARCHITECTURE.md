# Architecture

How Frosty Neon Studio is put together, for anyone changing the code. For the reasons behind the design and the ground rules, see [Design](docs/STAGE2_PLAN.md).

## Layers

```text
Frosty Neon Studio (tools/app)        Avatar Guide / Avatar Pipeline windows (Unity)
            │                                         │
            ▼                                         │
        frosty.ps1  ──────── runs Unity in batch mode ┤
            │                                         ▼
            │                     Unity editor tools (Assets/Editor/FrostyNeon)
            │                                         │
            └── runs Blender in the background ──► Blender tools (tools/blender)
```

- **Blender tools** (`tools/blender/`): standalone `bpy` scripts. A read-only scene audit and an FBX exporter driven by a versioned profile. They never apply, rotate or rescale anything in the source.
- **Unity editor tools** (`Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/`): C# editor scripts. Each tool can run in batch mode with `-executeMethod`, writes a JSON report to `Logs/FrostyNeon/`, and exits with 0 or 1. They are editor-only and never part of an avatar's runtime.
- **`frosty.ps1`**: one PowerShell entry point. It finds Unity and Blender, refuses to run while the Unity Editor has the project open, runs a tool, and prints its report in plain words.
- **Front ends**: Frosty Neon Studio (`tools/app/`), a WPF window that runs `frosty.ps1` in the background and reads its reports, and the Avatar Guide and Avatar Pipeline windows inside Unity, which call the same C# tools directly.

Because both front ends sit on the same tools, a fix in one tool reaches the app, the Guide and the command line at once.

## The Unity tools

| File | Role | Page |
|---|---|---|
| `AvatarPreflight.cs` | The check. Reads only. | [Avatar preflight](docs/AVATAR_PREFLIGHT.md) |
| `AvatarSetupHelper.cs` | Import settings for plain models | [Setup helper](docs/AVATAR_SETUP_HELPER.md) |
| `AvatarMaterialSetup.cs` | Mobile materials for plain models | [Material setup](docs/AVATAR_MATERIAL_SETUP.md) |
| `AvatarDescriptorSetup.cs` | The `_Avatar.prefab` with the VRChat descriptor | [Descriptor setup](docs/AVATAR_DESCRIPTOR_SETUP.md) |
| `AvatarPreviewRender.cs` | Preview pictures, height and eye position | [Avatar Guide](docs/AVATAR_GUIDE.md#step-3-fix-and-preview) |
| `AvatarPipeline.cs` | Runs the four tools above in order for one model | [Commands](docs/AVATAR_PIPELINE.md#the-avatar-pipeline) |
| `AvatarPackage.cs` | Lists and imports `.unitypackage` files, with the script check | [Avatar Guide](docs/AVATAR_GUIDE.md#step-1-pick-your-avatar) |
| `AvatarVroid.cs` | Reads VRM terms, brings `.vrm` files in through UniVRM | [Avatar Guide](docs/AVATAR_GUIDE.md#vroid-files) |
| `AvatarGuidedFix.cs` | The Fix for ready-made avatars, with Undo | [Avatar Guide](docs/AVATAR_GUIDE.md#the-fix) |
| `AvatarQuestVersion.cs` | Quest versions of ready-made avatars | [Avatar Guide](docs/AVATAR_GUIDE.md#quest-version) |
| `AvatarUpload.cs`, `AvatarUploadRequest.cs` | The uploader, the only tool that publishes | [Uploading](docs/AVATAR_UPLOAD.md) |
| `FrostyNeonCommands.cs` | `list`, `inspect`, `fix`, `quest` and `vroid` for `frosty.ps1` | [Commands](docs/AVATAR_PIPELINE.md) |
| `FindingText.cs` | Plain-language text for every finding code | |
| `FrostyNeonGuide.cs`, `FrostyNeonWindow.cs`, `FrostyTheme.cs` | The Avatar Guide, the Avatar Pipeline window, and their shared look | |
| `FbxImportProbe.cs`, `SdkAvatarProbe.cs` | Developer probes behind the measurements | [Measured in Unity](docs/UNITY_VALIDATION.md) |
| `*SelfTest.cs` | Self-tests, run by `frosty.ps1 selftest` | |

Only the setup helper, material setup, descriptor setup, Fix, Quest version, package and VRoid import change anything in the project, and each changes only import settings or adds new files beside the originals. Model files, `.blend` files and creators' prefabs are never edited.

## Repository layout

```text
frosty-neon-studio/
├── Frosty Neon Studio.cmd        # starts the app
├── frosty.ps1                    # command line
├── VERSION
├── Assets/
│   └── Avatars/
│       ├── Source/               # FrostyNeonBot.blend
│       └── Exports/{PC,Quest}/   # FBX files exported from Blender
├── brand/                        # logo, icons and social card (see brand/BRAND.md)
├── docs/                         # user and reference documentation
├── site/                         # the website (GitHub Pages)
├── tools/
│   ├── app/                      # Frosty Neon Studio (PowerShell + WPF)
│   ├── blender/                  # audit and exporter
│   │   ├── data/                 # versioned lookup tables: humanoid bone names, performance budgets
│   │   └── profiles/             # versioned FBX export settings
│   └── release/                  # public copy script and README
└── Unity/
    └── FrostyAvatarProject/      # the Creator Companion avatar project
        ├── Assets/Editor/FrostyNeon/   # the Unity tools
        ├── Assets/FrostyNeon/          # FrostyNeonBot, test fixtures; avatars people add go here too
        └── Packages/                   # vpm-manifest.json; Creator Companion installs the SDK
```

`Assets/Shared`, `Assets/Worlds`, `Unity/AvatarTemplate` and `Unity/Packages` are empty placeholders for later work and are not used yet.

**Not in Git:** Unity's `Library`, `Temp` and `Logs` folders; the VRChat SDK, UniVRM and community packages (Creator Companion or the package import restores them); and avatars people add (`Assets/FrostyNeon/Models`, `Assets/FrostyNeon/VRoid`, and exported avatars other than FrostyNeonBot).

## Decisions

- **Avatars first.** World tools are deferred.
- **PC and Quest are separate targets.** Every check reports each platform on its own. Passing on PC says nothing about Quest.
- **Varied art styles.** Checks are configurable (target ranks, bone-name tables) rather than tuned to one character.
- **Batch first.** Every Unity tool runs in batch mode, so the app, the command line and the self-tests all use the same code path. Only the upload needs the Unity window, because the VRChat SDK's builder exists only there.
- **Follow the installed SDK.** Where VRChat's web pages and SDK 3.10.6 disagree, the toolkit follows the SDK. See [VRChat versions and rules](docs/VRCHAT_COMPATIBILITY.md).
- **Blender to Unity coordinates.** The FBX profile writes a Y-up file without baking the axis conversion, so the imported armature carries a -90° X rotation and the model root none. The exporter never rotates or rescales the source. See [FBX export profile](docs/FBX_EXPORT_PROFILE.md).
- **Versioned data.** Bone names, performance limits and export settings live in versioned JSON files. Changing a value means a new version and a re-run of the self-tests.

## Tests

`.\frosty.ps1 selftest` runs every self-test: the two Blender tools, eight Unity self-tests in batch mode, and the app's self-test. Each self-test builds its own test models or uses the fixtures in `Assets/FrostyNeon/Fixtures`, and checks that it left them unchanged.
