# Tested with

These are the avatars the toolkit has been tested on, what happened, and what each one taught. Everything was run with Unity 2022.3.22f1 and VRChat SDK 3.10.6 on Windows.

The test avatars from other creators are not part of this repository.

## Summary

| Avatar | Kind | Result |
|---|---|---|
| FrostyNeonBot (included) | Blender, exported with the toolkit's FBX profile | Passes on PC and Quest. Uploaded for PC and Quest, and works in VRChat on PC. |
| Always Watching (100Avatars #068) | `.fbx`, Mixamo bone names, centimetres | Passes on PC and Quest after Prepare with `-Height`. Uploaded for PC and works in VRChat. |
| Skull (100Avatars #005) | same | Passes on PC and Quest. Uploaded for PC and works in VRChat. |
| Wire Friend (#022), Ghost (#034), Polybot (#051), Rabbit (#059) | same | Pass, Ghost and Polybot with notes. Not uploaded. |
| VRoid sample avatar P | VRoid Studio export, VRM 0.0 | Passes on PC with notes; its Quest version passes with notes. Not uploaded, because its terms allow only its author to use it. |
| VRoid sample avatar C | VRM 1.0 | Stopped with a message, as intended: VRM 1.0 is not supported yet. |
| Generated test models | Built by the self-tests | Cover the rig, size, material, Fix and Quest version cases. |

**Not yet tested:** Booth avatars, avatars with several materials and PC shaders such as lilToon or Poiyomi from start to finish, Blender-native rigs other than FrostyNeonBot, and any Quest version on a Quest headset. If you try one of these, please [report how it went](../CONTRIBUTING.md#reporting-a-problem).

## 100Avatars (free models in centimetres)

Six avatars from Polygonal Mind's free 100Avatars collection (round 1). Their `.vrm` headers give the licence as CC0. Each came as an `.fbx`, a texture and a `.vrm`. They were set up with `frosty.ps1 prepare <model> -Height <height>`, using the height from the `.vrm`.

| Avatar | Status after Prepare | Notes | PC / Quest rank | Visemes |
|---|---|---|---|---|
| #068 Always Watching | pass | none | Excellent / Excellent | 15 |
| #005 Skull | pass | none | Excellent / Excellent | 15 |
| #022 Wire Friend | pass | none | Excellent / Excellent | 15 |
| #034 Ghost | warn | A blend shape is switched on in the source file (`vrc.v_sil` at 1.12) | Excellent / Excellent | 15 |
| #051 Polybot | warn | The source's Hips bone is scaled 100; the avatar is 1.06 m tall | Excellent / Excellent | none |
| #059 Rabbit | pass | none | Excellent / Excellent | none |

The two warnings describe the source files accurately. They are not toolkit faults.

What these avatars found, all now handled by the toolkit:

- **Wrong size.** Always Watching's FBX declares centimetres, but its geometry is about 16 times too small. No safe automatic rule covers that, so the toolkit gained `-Height`. A model 100 times too small is still corrected automatically, and the check warns `model_very_small` for anything under 0.3 m.
- **Broken in VRChat after a clean upload.** Always Watching and Skull uploaded without errors but did not work in VRChat. VRChat's log measured the eyes more than a metre below the neck. The cause: with Convert Units on, Unity stored the humanoid skeleton 100 times too large. The setup helper now turns Convert Units off and moves the scale into Scale Factor, and the check reports the problem as `humanoid_rest_pose_scale_mismatch`. After re-uploading, both stand and look out of the right place.
- **Texture streaming.** Skull's first upload was refused by the SDK because a texture had mipmaps without Streaming Mip Maps. The material setup now turns it on, and the check reports it.
- **Viseme spelling.** Always Watching names the E viseme `vrc.v_ee`. Both spellings are now accepted.

Not the toolkit's to fix:

- **Texture file name.** Always Watching's FBX refers to `texture_068.png`, but the download names the image `100Avatars_068_AlwaysWatching.png`, so Unity imports the model untextured. Renaming the copy to `texture_068.png` beside the FBX fixes it. If your model arrives untextured, check whether its textures have the names the model expects.
- **Bind poses.** Rabbit's mesh bind poses are far from its bones (up to 1.44 m on the fingers). The check does not test bind poses yet, so check such an avatar in VRChat before relying on it.

**One unexplained upload.** One Skull upload stood half in the floor in VRChat, although it was built from the same files as uploads before and after it that stood correctly. Nothing unusual appeared in the Unity log. If an avatar sinks into the floor after a clean check, try uploading it again first.

## VRoid Studio samples

- **Sample avatar C** (VRM 1.0): the toolkit read its terms from the file (only the author may use it, no changes allowed) and stopped with a message that VRM 1.0 is not supported. Nothing was imported. This file led to the terms check that now runs before every VRoid import.
- **Sample avatar P**, exported from VRoid Studio 2.16.0 as VRM 0.0: UniVRM read it (16 MToon materials), and the toolkit set it up with jaw-flap lip sync from `Fcl_MTH_A` on the face mesh. PC passes with notes. The Fix had nothing to change. The Quest version converted 15 materials and capped 15 textures for Quest, and passes with two notes: performance below the Quest target, and an unusual leg bone order. Its face matches the PC version. Its cut-out clothing shows dark shapes on Quest at the collar, tie, cuffs and jacket hem, because no Quest shader can cut out.

The sample's terms allow only its author to use it as an avatar, so it was checked locally and not uploaded.

## FrostyNeonBot

The included starter avatar, made in Blender and exported with the toolkit's [FBX profile](FBX_EXPORT_PROFILE.md). It has 3,552 triangles, two materials and 21 humanoid bones, and ranks Excellent on PC and Medium on Quest. It has been uploaded for PC and for Quest and works in VRChat on PC; the Quest build has not yet been seen on a headset. It has no mouth shapes, so its lip sync is off.

A `.vrm` exported from FrostyNeonBot with UniVRM was also brought back in through the VRoid path. It measured the same height and eye height as the original, and its Quest version passed.

## How a test is run

1. The avatar is copied into the Unity project, outside the folders kept in Git.
2. It goes through the same path a user would take: `frosty.ps1 import`, `vroid` or `model`, then `inspect`, `fix`, `quest` and `upload` as needed, or the app.
3. The check, the preview pictures and, where the licence allows, the avatar in VRChat are compared with what the toolkit promised.
4. Anything the toolkit got wrong becomes a fix and, where possible, a self-test case.
