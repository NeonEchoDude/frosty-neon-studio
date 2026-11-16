# Your first avatar in VRChat

This page walks you from your avatar file to wearing the avatar in VRChat, using Frosty Neon Studio. It assumes you have finished [Start here](../START_HERE.md): Creator Companion is installed, the project has been opened once, and the app opens.

Before you start:

- **Close Unity.** The app runs Unity in the background and cannot while the Unity Editor has the project open.
- **Only upload avatars you may use.** If you bought an avatar, its licence usually allows you to wear it. If a VRoid file's terms say only its author may use it, do not upload it unless that is you.

Prefer to work inside Unity? The [Avatar Guide](AVATAR_GUIDE.md) has the same steps in a Unity window.

## 1. Add your avatar

Drag the file from File Explorer and drop it anywhere on the app window, or click **Add an avatar file** and choose it. While you drag a file over the window, it shows what dropping it will do.

What happens next depends on the file:

| Your file | What the app does |
|---|---|
| Booth package (`.unitypackage`) | Imports it and checks the avatars it contains. If the package contains scripts, it lists them and asks first. Say Yes only if you trust where the file came from, such as the creator's own Booth page, because Unity runs scripts as soon as they are imported. |
| VRoid Studio file (`.vrm`) | Shows the creator's terms stored in the file. If they limit who may use or change the avatar, it asks whether you are the author or have permission. Then it sets the avatar up for VRChat: eye position and lip sync. |
| Model (`.fbx`) | Asks first, then copies the model into the project, with its `Textures` or `<name>.fbm` folder if there is one, and sets it up as an avatar: humanoid rig, materials, eye position, and lip sync if the model has VRChat's 15 mouth shapes. Your own file is not changed. |
| Zip file (`.zip`) | Tells you which avatar files are inside. Unzip it first (right-click, **Extract All**), then add the avatar file from the unzipped folder. |
| Blender file (`.blend`) | Tells you to export it as FBX first. See [Commands](AVATAR_PIPELINE.md) for `frosty.ps1 all`, which does the export for you. |

Adding an avatar takes one to three minutes. When it is done, a message says whether it worked, and the new avatar is selected in the list.

**VRoid files** need two things first:

- Export the avatar from VRoid Studio as **VRM 0.0**. VRM 1.0 files are stopped with a message.
- The project needs UniVRM, a free Unity add-on that reads `.vrm` files. If it is missing, the app says so. Download the UniVRM `.unitypackage` for VRM 0.x from the [UniVRM releases page](https://github.com/vrm-c/UniVRM/releases) and add it to the app like any Booth package. It contains scripts, so the app asks first.

**Booth avatars** usually need a **shader package** as well, most often lilToon or Poiyomi. The creator's Booth page says which. If it is missing, the check in the next step says which shader the materials need. Add it through Creator Companion (**Manage Project**), or add the shader's own `.unitypackage` to the app.

## 2. Read the check

Click your avatar in the list. The app checks it and shows on the right:

- the avatar's **name** and a **headline**, such as "Looking fresh! Nothing to fix." or "The PC version is ready! Quest needs a bit more.";
- a **PC** badge and a **QUEST** badge, each saying **READY** or **NEEDS WORK**;
- **three pictures**: front, three-quarter and back. The cyan dashed line and ring mark where your eyes will be in VRChat;
- the avatar's **height** and **eye height**, and a note if it would sink into the floor or float above it;
- the **findings**, in up to three groups:
  - **Must fix before uploading**: something only you can sort out, such as a missing shader. Each finding says what to do.
  - **Fixed for you by the Fix step**: settings that **Fix it for me** changes.
  - **Good to know**: notes that do not stop the upload.

A finding marked **[quest only]** or **[pc only]** affects only that version.

Check the pictures: the avatar should stand upright, face you in the front picture, and have the eye mark at its eyes. If the eye mark is in the wrong place, your view in VRChat will be too.

## 3. Fix it, if offered

**Fix it for me** is available only when the avatar needs import settings changed before VRChat accepts it. These are Unity settings, not your avatar's files: mesh Read/Write, Legacy Blendshape Normals, and Streaming Mip Maps on textures.

1. Click **Fix it for me**. The app lists exactly what will change.
2. Click **Fix it**. When it finishes, the avatar is checked again.

You should see the **PC** badge change to **READY**, unless something under **Must fix** remains.

**Undo the fix** puts every setting back the way it was.

## 4. Make a Quest version, if offered

**Make a Quest version** is available when Quest cannot use the avatar's materials. Most Booth avatars use PC shaders, and Quest accepts only VRChat's own mobile shaders.

1. Click **Make a Quest version**, then **Make it**. The app creates a separate copy of the avatar with Quest materials and smaller Quest textures. Your PC avatar, its materials and its look are not changed.
2. Click **Show Quest look** to see the Quest version's pictures. Click **Show PC look** to switch back.

You should see the second badge change to **QUEST VERSION**.

Compare the Quest look with the PC look. Quest versions look simpler:

- See-through parts, such as eyelashes and eye highlights, use Quest's see-through shaders and can look different.
- Cut-out parts, such as lace or the edges of clothing, become solid, because no Quest shader can cut out. On some outfits this shows as dark shapes at collars, cuffs and hems.

The other buttons: **Make the Quest version again** rebuilds it, for example after you fix the PC avatar. **Remove the Quest version** deletes it and puts the texture settings back. An avatar already uploaded to VRChat is not affected.

Both versions upload to the **same avatar** in VRChat. Quest players see the Quest version, and PC players see the PC version.

## 5. Upload

1. Make sure Unity is closed.
2. Click **Upload for PC** (or **Upload for Quest**), then **Upload**. Uploads are always **private**: only you can wear the avatar.
3. **Unity opens by itself.** If the VRChat SDK window asks you to sign in, sign in there within five minutes. Unity remembers the sign-in for next time.
4. A **Frosty Neon upload** box appears in Unity. It names the VRChat account, the avatar and the version. Click **Upload** (for a new avatar) or **Update** (for one you uploaded before). The first time, VRChat may show its own upload agreement.
5. Wait. When the upload has finished, the app shows a message saying whether it worked. Unity stays open: close it when you are done, then click **Try again** in the app to refresh the avatar's check.

To upload both versions, do one, then the other. The app switches Unity between PC and Quest for you.

## 6. Wear it

In VRChat, open your menu, go to **Avatars**, look under **Uploaded** and select it. It can take a minute to appear.

Then check:

- **Stand still.** Your feet are on the floor, not sunk into it or floating above it.
- **Find a mirror.** Your view is at the avatar's eyes, and the mouth moves when you talk.
- **Wave.** The hands and fingers follow your controllers.
- **Quest version:** look at it on a Quest headset, or ask a friend on Quest how it looks.

Something off? Close VRChat. Its newest log file, `output_log_*.txt` in `%USERPROFILE%\AppData\LocalLow\VRChat\VRChat`, records what VRChat saw. Include it if you [report a problem](../CONTRIBUTING.md#reporting-a-problem).

## When something goes wrong

| What you see | What to do |
|---|---|
| "Unity has this project open" | Close Unity (save if it asks), wait a moment, then click **Try again** in the notice. |
| "The studio is still working on the last step" | Wait until the step shown in the top-right corner has finished. |
| **Upload for Quest** stays grey | Hover over it to see why. Usually the Quest version is missing or Unity's Android Build Support is not installed (see [Start here](../START_HERE.md#4-add-quest-support-optional)). |
| Materials are **pink** in the pictures | A shader package is missing. The check names it. Add it through Creator Companion (**Manage Project**) or with the shader's own `.unitypackage`. |
| A model is tiny, or the check says it is very small | The file uses unusual units. Set its real height with `.\frosty.ps1 model <file.fbx> -Height 1.6` (in metres). See [Commands](AVATAR_PIPELINE.md). |
| A model shows no textures | Its textures may have a different file name from the one the model expects. Check the creator's instructions; some downloads need the texture renamed or placed in a `Textures` folder beside the `.fbx`. |
| Unity opens in **Safe Mode** | A script in the project does not compile, usually from an imported package. Remove or update that package before leaving Safe Mode. |
| "Nobody is signed in to VRChat" | Sign in on the **Authentication** tab of the VRChat SDK window in Unity, then upload again. |
| The Quest upload says the avatar is **too large** | Quest allows 10 MB per avatar. The Quest version already makes textures smaller. Going further means removing outfits or props in Unity, then rebuilding the Quest version. |
| Quest players see a **fallback** avatar | The Quest version is over VRChat's Quest performance limits. It still uploads; other players see it if they choose to show it. |

Everything the app did is in the **ACTIVITY** log at the bottom of the window, and **More > Open the logs folder** opens the full logs and reports. When something goes wrong, a red notice at the top of the window says what to do. For help, click **Copy details** under the avatar's results (or **More > Copy details for a helper**) and paste the text into your report.
