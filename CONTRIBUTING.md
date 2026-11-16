# Contributing

Thank you for helping make Frosty Neon Studio better. Reports from people trying it on their own avatars are the most useful contribution of all.

## Reporting a problem

Open an issue with the **Bug report** form. Please include:

- what you did, what you expected, and what happened instead;
- the details from the app: choose **More > Copy details for a helper** and paste them into the form. They include the technical codes and the end of the activity log, without your Windows user name or folders;
- what kind of avatar it was (Booth, VRoid or `.fbx`) and, if it is free, where to get it, so the problem can be reproduced;
- for a problem in VRChat, VRChat's newest `output_log_*.txt` from `%USERPROFILE%\AppData\LocalLow\VRChat\VRChat`.

Please do not attach avatars you bought. Creators' terms usually forbid sharing them. A link to the shop page is enough.

## Suggesting something

Open an issue saying what you would like the app to do and why. Plain words are fine. The [Roadmap](docs/ROADMAP.md) lists what is already being considered.

## Changing the code

Start with [ARCHITECTURE.md](ARCHITECTURE.md) for how the pieces fit together.

- Everything runs on Windows. The app is Windows PowerShell 5.1 with WPF (`tools/app/`), the Unity side is C# editor scripts (`Unity/FrostyAvatarProject/Assets/Editor/FrostyNeon/`), the command line is `frosty.ps1`, and the Blender side is Python (`tools/blender/`).
- Before sending a change, close Unity and run every self-test with `.\frosty.ps1 selftest`. It needs both Unity and Blender installed. Add a self-test check for what you change.
- Keep the ground rules: never edit an avatar's own files, never upload without the person's confirmation, keep uploads private, and never install software automatically.
- Keep editor code in the editor folder. Nothing under `Assets/Editor` may be referenced from runtime code.
- `tools/app/FrostyNeonStudio.ps1` must stay plain ASCII, because Windows PowerShell 5.1 reads a script without a byte-order mark as ANSI.
- If you change what a command, button or check does, update its page in `docs/`.

By contributing, you agree that your contribution is licensed under the MIT licence (`LICENSE`).
