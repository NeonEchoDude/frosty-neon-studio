# Design: a toolkit for people who have never used Unity

This page explains who Frosty Neon Studio is for, the problems it solves, and the rules it keeps. What is planned next is in the [Roadmap](ROADMAP.md).

## Who it is for

Someone who has bought or downloaded an avatar, from Booth, VRoid Studio or a free collection, and wants to wear it in VRChat, but has never used Unity or Blender.

What they usually have:

| Source | What they download | How the toolkit handles it |
|---|---|---|
| Booth (most paid avatars) | A `.unitypackage` with a ready avatar prefab, materials and textures, usually needing a shader package such as lilToon or Poiyomi | Import with a script warning, missing-shader detection, the Fix, and a Quest version |
| VRoid Studio | A `.vrm` file | Read the creator's terms, then set it up through UniVRM, with jaw-flap lip sync |
| Free models, commissions, Mixamo | An `.fbx`, sometimes with a `.blend` | The Avatar Pipeline: import settings, materials, avatar prefab |

What they do not know about, and should not have to: Creator Companion, build targets, the SDK panel, Quest limits, or what an error like "Missing Mip Maps streaming" means.

## How it is built

The toolkit grew in layers, each usable on its own:

1. **Blender tools**: an audit and a tested FBX export profile, for avatars made in Blender.
2. **Unity tools**, run in batch mode: the check (preflight), the setup steps of the Avatar Pipeline, the Fix, the Quest version, package and VRoid import, preview pictures, and the uploader.
3. **`frosty.ps1`**: every Unity tool as one PowerShell command, each writing a JSON report.
4. **Two front ends over the same commands**: the Avatar Guide inside Unity, and Frosty Neon Studio, a Windows app that never shows Unity except for the upload.

Unity and Creator Companion still have to be installed. The VRChat SDK cannot be bundled, and VRChat's sign-in and upload agreement are for the account owner. So the app's job is to make Unity invisible after the first setup.

## The guided steps

Both front ends follow the same order, each step with one obvious button:

1. **Pick your avatar:** drop a file or pick one already in the project.
2. **Check:** the result in plain words, grouped as "must fix", "fixed for you" and "good to know", each saying what it means in VRChat ("your avatar would stand in the floor", "Quest players would see a fallback"). Technical codes stay available in a folded section.
3. **Fix:** one button for everything the toolkit can safely change, and one to undo it.
4. **Preview:** front, three-quarter and back pictures, with the height and the eye position marked.
5. **Upload:** PC and Quest buttons that switch Unity's build target themselves and end in VRChat's own confirmation.
6. **After the upload:** a short list of what to check in VRChat.

Every message says what to click, not what went wrong inside.

## Ground rules

- Nothing is uploaded without the person's confirmation, and uploads are always private.
- Source files (FBX, Blender files, the creator's prefab) are never modified. Changes go into Unity's import settings and new files beside the originals, and can be undone or removed.
- Nothing is installed by the toolkit. It says what to install and where to get it.
- A package with scripts is not imported until the person agrees, because Unity runs scripts on import.
- A creator's terms stored in a VRoid file are shown before set-up and respected at upload.
- Every fix is checked in Unity and, for anything that affects how the avatar behaves, in VRChat.
