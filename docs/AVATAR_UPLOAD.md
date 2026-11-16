# Uploading to VRChat

The uploader (`AvatarUpload.cs`) is the only part of the toolkit that publishes anything. It uploads an avatar prefab to the VRChat account signed in to the VRChat SDK window, always as a **private** avatar, and only after you confirm.

You can start an upload from:

- Frosty Neon Studio: **Upload for PC** or **Upload for Quest**;
- the Avatar Guide, step 4: **» Upload for PC** or **» Upload for Quest**;
- the Avatar Pipeline window: **Upload PC** or **Upload Quest**;
- PowerShell: `.\frosty.ps1 upload <model or prefab>`, with `-Quest` for the Quest version.

All of them use the same uploader.

## Before your first upload

- Your VRChat account must be allowed to upload avatars. New accounts need to spend some time in VRChat first. If yours cannot upload yet, the VRChat SDK says so and nothing is uploaded.
- For Quest uploads, Unity needs **Android Build Support** (see [Start here](../START_HERE.md#4-add-quest-support-optional)).
- Only upload avatars you may use. If the avatar came from a VRoid file whose terms say only its author may use it, the uploader asks you to confirm that you are the author or have permission.

## How an upload works

PC and Quest are two builds of **one** VRChat avatar. Both upload to the same avatar ID, so PC players see the PC build and Quest players see the Quest build. If the avatar has a Quest version (made with **Make a Quest version**), Quest uploads use it.

The uploader, in order, stopping at the first problem:

1. Checks that Unity is set to the right platform: Windows for PC, Android for Quest. In the Avatar Guide and the Pipeline window, it offers to switch. Unity then reloads its scripts, and you press the upload button again. From the app and `frosty.ps1 upload`, the switch happens before Unity opens, so there is nothing to press twice.
2. Gives the PC and Quest versions the same avatar ID, if only one of them has one.
3. Checks the avatar for this platform and stops if the check fails. A Quest-only problem does not block a PC upload, and the reverse.
4. Opens the VRChat SDK window. If nobody is signed in, it says so and stops. Sign in on the **Authentication** tab, then upload again.
5. Asks you to confirm in a **Frosty Neon upload** box that names the account, the avatar, the version and that it will be private. Click **Upload** for a new avatar, or **Update** for one uploaded before. VRChat may show its own upload agreement the first time.
6. Takes a picture for a new avatar's thumbnail, places the avatar in a scene of its own, and builds and uploads it with the VRChat SDK.
7. Stores the avatar ID VRChat assigned on the prefab, so the next upload updates the same avatar instead of creating another one.

An update keeps the avatar's name, description, visibility and picture on VRChat.

Every upload writes `Unity\FrostyAvatarProject\Logs\FrostyNeon\upload_result.json` with the outcome and any error.

## From PowerShell: `frosty.ps1 upload`

```powershell
.\frosty.ps1 upload Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab          # PC version
.\frosty.ps1 upload Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab -Quest   # Quest version
.\frosty.ps1 upload <model or prefab> -DryRun                                      # check only, nothing uploaded
.\frosty.ps1 upload <model> -Name "My Avatar"                                      # name for a new avatar
```

Give the avatar prefab, or the model it was prepared from (the command then uses `<model>_Avatar.prefab` beside it). Close Unity first. The command:

1. switches Unity to Windows or Android in batch mode, and checks the avatar for that platform;
2. lists the problems for that platform and stops if the check fails;
3. says whether it will update an existing avatar or upload a new one (with `-DryRun`, it stops here);
4. opens Unity. When Unity has loaded, it opens the VRChat SDK window, waits up to five minutes for the sign-in, and starts the upload. **The confirmation box still appears. Nothing is uploaded until you click Upload or Update.** If Unity asks to save a scene, save it anywhere in `Assets`;
5. waits up to 30 minutes for the result and prints it. It exits with code 0 only if the avatar was uploaded.

Unity is left open afterwards, set to the platform you last uploaded.

The request that Unity picks up is a file, `Logs\FrostyNeon\upload_request.json`. It is used once, and only within 30 minutes of being written. A request for the wrong platform, an expired request, or a missing sign-in ends as "not uploaded". Unity started in batch mode, as every other command does, ignores the request.

## From the Unity menu

**Frosty Neon > Upload FrostyNeonBot To VRChat - PC (Private)** and **... - Quest (Private)** upload the starter avatar, FrostyNeonBot. For any other avatar, use the Avatar Guide, the Pipeline window or `frosty.ps1 upload`.

## Why the upload happens in the Unity window

- The VRChat SDK's builder only exists while the VRChat SDK window is open, so it cannot run in batch mode.
- Signing in to VRChat and accepting VRChat's upload agreement are for you to do, not a script.

## Readiness check

To check whether an avatar is ready to upload without uploading anything, use `.\frosty.ps1 upload <prefab> -DryRun`. Behind it is `AvatarUpload.CheckReady`, which writes `Logs\FrostyNeon\upload_readiness.json` and exits with 0 when the prefab has an avatar descriptor and the check does not fail.

## If an upload fails

- If a first upload fails after VRChat has already created the avatar record, the record's ID is kept on the prefab, so the next attempt updates that record instead of creating a second avatar. If an empty avatar is left on your account, you can delete it on the VRChat website.
- Send the message the upload showed, and `upload_result.json`, with your [bug report](../CONTRIBUTING.md#reporting-a-problem). Retrying many times does not help.

## Tested

- PC uploads of new avatars, PC updates of existing avatars, and a Quest upload of FrostyNeonBot, from the menu, the Pipeline window and `frosty.ps1 upload`.
- The upload request self-test (`AvatarUploadRequestSelfTest`, part of `frosty.ps1 selftest`): valid requests accepted; old, future-dated, unknown-platform, nameless, missing and descriptor-less requests refused; PC and Quest readiness reported separately.
- Not yet tested: `-Name` for a new avatar from PowerShell, a Quest upload from `frosty.ps1 upload` (the same upload has run from Unity), the five-minute wait without a sign-in, and how a Quest version looks on a Quest headset.
- The check covers only some of the VRChat SDK's own upload checks. If the SDK refuses an avatar the check passed, please report it.
