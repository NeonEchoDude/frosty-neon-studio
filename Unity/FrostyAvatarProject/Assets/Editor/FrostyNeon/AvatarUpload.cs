// Frosty Neon avatar upload (Editor-only). The ONLY Frosty Neon tool that publishes anything.
//
// It uploads the starter avatar to the VRChat account that is signed in to the VRChat SDK panel,
// as a PRIVATE avatar, through the SDK's public builder API (IVRCSdkAvatarBuilderApi.BuildAndUpload).
// It cannot run in batch mode: the SDK builder only exists while the SDK control panel window is
// open, and signing in and accepting VRChat's upload agreement are things only the account owner
// can do. So the upload is a menu item the owner clicks, one per platform:
//
//   Frosty Neon > Upload FrostyNeonBot To VRChat - PC (Private)
//   Frosty Neon > Upload FrostyNeonBot To VRChat - Quest (Private)
//
// VRChat keeps one avatar with a separate build per platform; both menu items upload to the same
// avatar ID. The SDK builds for Unity's active build target (Windows for PC, Android for Quest).
// If the target does not match, the menu item offers to switch it. Switching reloads every script
// in Unity, so after the switch the owner chooses the menu item again.
//
// What it does, in order, stopping at the first problem:
//   1. Opens the VRChat SDK control panel. If nobody is signed in, it says so and stops.
//   2. Runs the preflight on the avatar prefab. If the status is fail, it stops.
//   3. Asks for confirmation, naming the account, the avatar name and that it will be private.
//   4. Renders a thumbnail, puts the prefab in a scene of its own (a temporary one beside a saved open
//      scene, or in place of an untitled scene that holds only Unity's default camera and light; an
//      untitled scene with real content stops the upload instead of being discarded), and calls the
//      SDK's BuildAndUpload. No save prompt appears.
//      The SDK shows its own upload agreement the first time; that is the owner's to accept.
//   5. On success, stores the avatar ID VRChat assigned on the prefab, so the next upload updates
//      the same avatar instead of creating another. An update fetches the avatar's current record
//      from VRChat first, so its name, description and visibility are kept, and keeps its picture.
// Every run writes Logs/FrostyNeon/upload_result.json with what happened.
//
// AvatarUpload.CheckReady (batch mode, Editor closed) reports whether the pieces are in place
// without uploading anything.
//
// Written against VRChat SDK 3.10.6. Run on 2026-11-15: PC upload as a new avatar (FrostyNeonBot), PC updates
// of existing avatars (Skull, from the pipeline window), and the Quest upload of FrostyNeonBot (an update of the
// same avatar, Android build target).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace FrostyNeon.Editor
{
    public static class AvatarUpload
    {
        const string PrefabPath = "Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab";
        const string AvatarName = "FrostyNeonBot";
        const string AvatarDescription = "Frosty Neon VR Studio starter avatar.";
        const string ReleaseStatus = "private";
        public const string ResultPath = "Logs/FrostyNeon/upload_result.json";
        const string PreviewDir = "Logs/FrostyNeon/previews";
        const string Title = "Frosty Neon upload";

        [MenuItem("Frosty Neon/Upload FrostyNeonBot To VRChat - PC (Private)")]
        public static void UploadPcFromMenu()
        {
            Upload(PrefabPath, AvatarName, false);
        }

        [MenuItem("Frosty Neon/Upload FrostyNeonBot To VRChat - Quest (Private)")]
        public static void UploadQuestFromMenu()
        {
            Upload(PrefabPath, AvatarName, true);
        }

        /// <summary>Uploads an avatar prefab as a private avatar for PC or Quest. Used by the menu items and the pipeline window.</summary>
        public static async void Upload(string prefabPath, string avatarName, bool quest)
        {
            string platform = quest ? "Quest" : "PC";
            BuildTarget wanted = quest ? BuildTarget.Android : BuildTarget.StandaloneWindows64;
            BuildTargetGroup group = quest ? BuildTargetGroup.Android : BuildTargetGroup.Standalone;
            if (string.IsNullOrWhiteSpace(avatarName))
            {
                EditorUtility.DisplayDialog(Title, "Give the avatar a name first.", "OK");
                return;
            }
            // A Quest upload of an avatar that has a Quest version (AvatarQuestVersion) uploads that version instead.
            string questVersion = quest ? AvatarQuestVersion.QuestPrefabFor(prefabPath) : null;
            if (questVersion != null) prefabPath = questVersion;
            var result = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-upload",
                ["platform"] = platform,
                ["prefab"] = prefabPath,
                ["avatar_name"] = avatarName,
                ["release_status"] = ReleaseStatus,
                ["uploaded"] = false,
            };
            GameObject instance = null;
            Scene uploadScene = default;
            Scene restoreActive = default;
            bool restoreDefaultScene = false;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != wanted)
                {
                    result["active_build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString();
                    if (!EditorUtility.DisplayDialog(Title,
                            "A " + platform + " upload needs Unity's build target set to " + wanted + "; it is " +
                            EditorUserBuildSettings.activeBuildTarget + " now.\n\nSwitch it? Unity will reload all scripts " +
                            "(this can take a minute the first time). Then choose this menu item again.", "Switch", "Cancel"))
                    {
                        Stop(result, "Cancelled; nothing was changed or uploaded.", dialog: false);
                        return;
                    }
                    Stop(result, "Switching the build target to " + wanted + ". When Unity has finished reloading, choose the " +
                                 platform + " upload menu item again.", dialog: false);
                    EditorUserBuildSettings.SwitchActiveBuildTarget(group, wanted);
                    return;
                }

                // Both versions keep one VRChat avatar ID, so PC and Quest go to the same avatar.
                string idNote = AvatarQuestVersion.SyncIds(prefabPath);
                if (idNote != null) result["avatar_id_note"] = idNote;
                Dictionary<string, object> ready = Readiness(prefabPath);
                result["readiness"] = ready;
                if (!(bool)ready["prefab_ok"])
                {
                    Stop(result, "The avatar prefab is missing or has no descriptor: " + prefabPath);
                    return;
                }
                // Only this platform's result counts: a PC upload is not blocked by a Quest-only problem (and the reverse).
                string platformStatus = (string)ready[quest ? "quest_status" : "pc_status"];
                if (platformStatus == "fail")
                {
                    Stop(result, "The preflight reports problems with the avatar for " + platform + "; nothing was uploaded. See " + ResultPath + ".");
                    return;
                }

                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                if (APIUser.CurrentUser == null)
                {
                    Stop(result, "Nobody is signed in to VRChat. Sign in on the Authentication tab of the VRChat SDK window that " +
                                 "just opened, then choose this menu item again.");
                    return;
                }
                string account = APIUser.CurrentUser.displayName;
                result["account"] = account;
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder))
                {
                    Stop(result, "The VRChat SDK builder is not available. Open the Builder tab of the VRChat SDK window, then try again.");
                    return;
                }

                // An avatar made from a VRoid/VRM file carries its creator's terms; "only the author" is asked about here.
                AvatarVroid.VrmInfo terms = AvatarVroid.TermsFor(prefabPath);
                if (terms != null)
                {
                    result["vrm_terms"] = terms.Lines().Cast<object>().ToList();
                    if (terms.OnlyAuthorMayUse && !EditorUtility.DisplayDialog(Title,
                            "This avatar comes from \"" + terms.Title + "\" by " + terms.Author + ". Its terms say only its author may use it " +
                            "as an avatar.\n\nUpload it only if you are the author or have their permission.",
                            "I'm the author / I have permission", "Cancel"))
                    {
                        Stop(result, "Cancelled: the avatar's terms allow only its author to use it. Nothing was uploaded.", dialog: false);
                        return;
                    }
                }

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<PipelineManager>();
                bool update = existing != null && !string.IsNullOrEmpty(existing.blueprintId);
                if (!EditorUtility.DisplayDialog(Title,
                        (update ? "Update the existing avatar" : "Upload a NEW avatar") + " named \"" + avatarName + "\" (" + platform +
                        " version) on the VRChat account \"" + account + "\" as a PRIVATE avatar?\n\nThis publishes the avatar to that account. VRChat may show its own " +
                        "upload agreement next.", update ? "Update" : "Upload", "Cancel"))
                {
                    Stop(result, "Cancelled; nothing was uploaded.", dialog: false);
                    return;
                }

                // A new avatar needs a picture; an update keeps the existing record and picture.
                VRCAvatar avatar;
                string thumbnail = null;
                if (update)
                {
                    avatar = await VRCApi.GetAvatar(existing.blueprintId, true);
                    result["updated_existing_record"] = avatar.ID;
                }
                else
                {
                    thumbnail = AvatarPreviewRender.Render(prefabPath, PreviewDir, 1200)[0];
                    avatar = new VRCAvatar
                    {
                        Name = avatarName,
                        Description = prefabPath == PrefabPath ? AvatarDescription : "",
                        Tags = new List<string>(),
                        ReleaseStatus = ReleaseStatus,
                    };
                }
                result["thumbnail"] = thumbnail;
                // The avatar needs a scene of its own. Unity refuses to add a scene beside an unsaved untitled
                // one, so:
                //  * open scene saved to disk -> add a temporary scene beside it, close it afterwards;
                //  * open scene untitled and holding nothing but Unity's default camera and light -> replace it
                //    (nothing is lost) and give back a fresh default scene afterwards;
                //  * otherwise (untitled with real content) -> stop and ask, rather than discard anything.
                Scene previous = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(previous.path))
                {
                    uploadScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    SceneManager.SetActiveScene(uploadScene);
                    restoreActive = previous;
                }
                else if (OnlyDefaultContent(previous) && SceneManager.sceneCount == 1)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    restoreDefaultScene = true;
                }
                else
                {
                    Stop(result, "The open scene is unsaved and has content in it. Save it (File > Save) or open a new " +
                                 "scene (File > New Scene), then choose this menu item again. Nothing was uploaded.");
                    return;
                }
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),
                                                                           SceneManager.GetActiveScene());

                await builder.BuildAndUpload(instance, avatar, thumbnail);

                string id = instance != null ? instance.GetComponent<PipelineManager>().blueprintId : null;
                result["uploaded"] = true;
                result["avatar_id"] = id;
                if (!string.IsNullOrEmpty(id))
                {
                    using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                        scope.prefabContentsRoot.GetComponent<PipelineManager>().blueprintId = id;
                    result["avatar_id_saved_on_prefab"] = true;
                    AvatarQuestVersion.SyncIds(prefabPath);
                }
                result["release_status_now"] = avatar.ReleaseStatus;
                Stop(result, "Uploaded the " + platform + " version of \"" + avatarName + "\" to \"" + account + "\"." +
                             (string.IsNullOrEmpty(id) ? "" : "\nAvatar ID: " + id));
            }
            catch (Exception error)
            {
                result["error"] = error.GetType().Name + ": " + error.Message;
                Debug.LogException(error);
                // VRChat may already have created the avatar record before the build failed. Keep its ID on the
                // prefab, so the next attempt updates that record instead of creating a second avatar.
                string reserved = instance ? instance.GetComponent<PipelineManager>().blueprintId : null;
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (!string.IsNullOrEmpty(reserved) && saved && string.IsNullOrEmpty(saved.GetComponent<PipelineManager>().blueprintId))
                {
                    using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                        scope.prefabContentsRoot.GetComponent<PipelineManager>().blueprintId = reserved;
                    result["reserved_avatar_id_kept_on_prefab"] = reserved;
                    AvatarQuestVersion.SyncIds(prefabPath);
                }
                Stop(result, "The upload did not finish:\n" + error.Message + "\n\nNothing else was changed. Details are in " + ResultPath + ".");
            }
            finally
            {
                if (restoreActive.IsValid() && restoreActive.isLoaded) SceneManager.SetActiveScene(restoreActive);
                if (uploadScene.IsValid() && uploadScene.isLoaded) EditorSceneManager.CloseScene(uploadScene, true);
                if (restoreDefaultScene) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }
        }

        /// <summary>True when the scene holds nothing, or only Unity's default Main Camera and Directional Light.</summary>
        static bool OnlyDefaultContent(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                bool camera = root.name == "Main Camera" && root.GetComponent<Camera>() != null;
                bool light = root.name == "Directional Light" && root.GetComponent<Light>() != null;
                if (!(camera || light) || root.transform.childCount > 0) return false;
            }
            return true;
        }

        static void Stop(Dictionary<string, object> result, string message, bool dialog = true)
        {
            result["message"] = message;
            AvatarPreflight.WriteReport(result, ResultPath);
            Debug.Log(Title + ": " + message);
            if (dialog && !Application.isBatchMode) EditorUtility.DisplayDialog(Title, message, "OK");
        }

        /// <summary>What can be checked without the SDK window or a sign-in.</summary>
        public static Dictionary<string, object> Readiness(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool prefabOk = prefab != null && prefab.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() != null &&
                            prefab.GetComponent<PipelineManager>() != null;
            var ready = new Dictionary<string, object>
            {
                ["prefab_ok"] = prefabOk,
                ["signed_in"] = APIUser.CurrentUser != null,
                ["batch_mode"] = Application.isBatchMode,
                ["avatar_id_already_set"] = prefabOk && !string.IsNullOrEmpty(prefab.GetComponent<PipelineManager>().blueprintId),
                ["active_build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
            };
            if (prefabOk)
            {
                Dictionary<string, object> preflight = AvatarPreflight.AnalyzeAsset(prefabPath, null);
                ready["preflight_status"] = preflight["status"];
                var platforms = (Dictionary<string, object>)preflight["platforms"];
                ready["pc_status"] = ((Dictionary<string, object>)platforms["pc"])["status"];
                ready["quest_status"] = ((Dictionary<string, object>)platforms["quest"])["status"];
                ready["preflight_findings"] = preflight["findings"];
            }
            return ready;
        }

        // Batch entry point: reports readiness, uploads nothing. Exit code 0 = prefab and preflight are in order.
        // -uploadPrefab <Assets/...prefab> checks that prefab (default: the starter avatar). Used by frosty.ps1 upload.
        public static void CheckReady()
        {
            int code = 1;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-uploadPrefab");
                string prefabPath = at >= 0 && at + 1 < args.Length ? args[at + 1] : PrefabPath;
                Dictionary<string, object> ready = Readiness(prefabPath);
                ready["prefab"] = prefabPath;
                ready["tool"] = "frosty-neon-unity-avatar-upload-readiness";
                ready["note"] = "Nothing was uploaded. Uploading needs the Unity Editor window and the owner's VRChat sign-in: " +
                                "frosty.ps1 upload, the Avatar Pipeline window, or the menu item.";
                string written = AvatarPreflight.WriteReport(ready, "Logs/FrostyNeon/upload_readiness.json");
                Debug.Log(Title + " readiness written to " + written);
                // Exit 0 when the prefab is in order and at least one platform does not fail; frosty.ps1 reads the
                // per-platform status (pc_status, quest_status) from the report.
                code = (bool)ready["prefab_ok"] && ((string)ready["pc_status"] != "fail" || (string)ready["quest_status"] != "fail") ? 0 : 1;
            }
            catch (Exception error)
            {
                Debug.LogError(Title + " readiness check failed: " + error);
            }
            EditorApplication.Exit(code);
        }
    }
}
