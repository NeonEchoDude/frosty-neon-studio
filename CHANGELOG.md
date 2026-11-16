# Changelog

## 0.1.0 (beta): first public release

### Frosty Neon Studio

A Windows app that takes an avatar to VRChat without opening Unity. Start it with `Frosty Neon Studio.cmd`.

- **PC check:** what to install and in which order (Creator Companion, Unity 2022.3.22f1, Quest support, the VRChat SDK). Installs nothing itself.
- **Add an avatar** with a button or by dropping the file on the window:
  - Booth packages (`.unitypackage`): the contents are listed first, and a package with scripts asks before it is imported;
  - VRoid Studio files (`.vrm`, VRM 0.0): the creator's terms are shown first;
  - plain models (`.fbx`): copied in with their texture folder and set up as an avatar;
  - a `.zip` is looked into, and you are told which avatar files are inside.
- **The check** in plain words, with front, three-quarter and back pictures and the eye height marked.
- **Fix it for me:** changes only import settings, and can be undone.
- **Make a Quest version:** a separate copy with Quest materials and smaller Quest textures, which can be removed.
- **Upload** for PC and Quest, always private, confirmed in Unity before anything is sent.
- **Details for helpers:** the technical codes and the end of the activity log, copied with one click for bug reports, without your user name or folders.
- Plain-language notices with **Try again**, explanations on greyed-out buttons, keyboard shortcuts, and a desktop shortcut.

### Also included

- The **Avatar Guide**, a Unity window with the same steps (**Frosty Neon > Avatar Guide (start here)**).
- The **Avatar Pipeline** window for plain models (**Frosty Neon > Avatar Pipeline**).
- `frosty.ps1`, with every step as a PowerShell command, plus `doctor` and `selftest`.
- Blender tools: a scene audit and a tested FBX export profile (`frosty.ps1 all <file.blend>`).
- **FrostyNeonBot**, a starter avatar under CC BY 4.0.

### Known limits

- Tested on few Booth avatars so far; see [Tested with](docs/TEST_AVATARS.md).
- Quest versions have not yet been checked on a Quest headset.
- VRM 1.0 files are not supported.
- Windows only.
