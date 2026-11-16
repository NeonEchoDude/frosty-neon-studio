// Frosty Neon upload request (Editor-only): lets `frosty.ps1 upload` start an upload without anyone looking for
// menus. It does NOT upload anything by itself: it calls AvatarUpload.Upload, which still asks the owner to confirm.
//
// How it works:
//   1. frosty.ps1 writes Logs/FrostyNeon/upload_request.json (prefab, avatar name, platform, time) and opens Unity.
//   2. When the Editor has loaded (not in batch mode), this class finds the request, opens the VRChat SDK window
//      (which restores the saved sign-in) and waits for the sign-in, up to 5 minutes.
//   3. It deletes the request (so it runs once) and calls AvatarUpload.Upload for that prefab and platform.
//      AvatarUpload shows its confirmation ("Update the existing avatar ..." or "Upload a NEW avatar ...") and
//      writes Logs/FrostyNeon/upload_result.json, which frosty.ps1 is waiting for.
//
// Safety:
//   * A request older than 30 minutes is discarded without uploading (for example if Unity is opened days later).
//   * A request for the wrong build target is not acted on: frosty.ps1 switches the target first; if it still does
//     not match, the request is discarded and the reason is written to upload_result.json.
//   * Anything that stops the request (expired, invalid, no sign-in) is written to upload_result.json, so frosty.ps1
//     always gets an answer.
//
// Added 2026-11-15 after two uploads were started this way by hand (Skull PC update, FrostyNeonBot Quest).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;

namespace FrostyNeon.Editor
{
    [InitializeOnLoad]
    public static class AvatarUploadRequest
    {
        public const string RequestPath = "Logs/FrostyNeon/upload_request.json";
        const string ResultPath = "Logs/FrostyNeon/upload_result.json";
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
        const double SignInWaitSeconds = 300;

        [Serializable]
        public class Request
        {
            public string prefab;
            public string name;
            public string platform;       // "pc" or "quest"
            public string requested_at;   // UTC, ISO 8601 ("o" format)
        }

        static double _start = -1, _panelOpened = -1;

        static AvatarUploadRequest()
        {
            if (!Application.isBatchMode && File.Exists(RequestPath)) EditorApplication.update += Tick;
        }

        /// <summary>Reads and checks a request. Returns null and a reason when it must not be acted on.</summary>
        public static Request Read(string path, DateTime nowUtc, out string problem)
        {
            problem = null;
            Request request;
            try { request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); }
            catch (Exception error) { problem = "The upload request could not be read: " + error.Message; return null; }
            if (request == null) { problem = "The upload request is empty."; return null; }
            if (!DateTime.TryParse(request.requested_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime when))
            {
                problem = "The upload request has no valid time."; return null;
            }
            TimeSpan age = nowUtc - when.ToUniversalTime();
            if (age > MaxAge || age < TimeSpan.FromMinutes(-5))
            {
                problem = "The upload request is dated " + when.ToUniversalTime().ToString("u") + ", not within the last 30 minutes, " +
                          "so it was not acted on; nothing was uploaded. Run frosty.ps1 upload again.";
                return null;
            }
            if (request.platform != "pc" && request.platform != "quest") { problem = "Unknown platform \"" + request.platform + "\" (use pc or quest)."; return null; }
            if (string.IsNullOrWhiteSpace(request.name)) { problem = "The upload request has no avatar name."; return null; }
            var prefab = string.IsNullOrEmpty(request.prefab) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(request.prefab);
            if (prefab == null || prefab.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() == null)
            {
                problem = "No avatar prefab with a VRChat avatar descriptor at " + request.prefab + "."; return null;
            }
            return request;
        }

        public static BuildTarget TargetFor(string platform) => platform == "quest" ? BuildTarget.Android : BuildTarget.StandaloneWindows64;

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            double now = EditorApplication.timeSinceStartup;
            if (_start < 0) _start = now;
            if (now - _start < 5) return;                                   // let the Editor finish opening
            if (!File.Exists(RequestPath)) { EditorApplication.update -= Tick; return; }

            if (_panelOpened < 0)
            {
                Request check = Read(RequestPath, DateTime.UtcNow, out string problem);
                if (check == null) { Finish(problem, null); return; }
                if (EditorUserBuildSettings.activeBuildTarget != TargetFor(check.platform))
                {
                    Finish("Unity's build target is " + EditorUserBuildSettings.activeBuildTarget + ", but a " + check.platform +
                           " upload needs " + TargetFor(check.platform) + ". frosty.ps1 upload switches it before opening Unity; " +
                           "run it again with Unity closed. Nothing was uploaded.", check);
                    return;
                }
                _panelOpened = now;
                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");   // restores the saved sign-in
                return;
            }

            bool signedIn = APIUser.CurrentUser != null && VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _);
            if (!signedIn && now - _panelOpened < SignInWaitSeconds) return;
            Request request = Read(RequestPath, DateTime.UtcNow, out string again);
            if (request == null) { Finish(again, null); return; }
            if (!signedIn)
            {
                Finish("VRChat did not sign in within 5 minutes. Sign in on the VRChat SDK window's Authentication tab, then " +
                       "run frosty.ps1 upload again (or use Frosty Neon > Avatar Pipeline). Nothing was uploaded.", request, dialog: true);
                return;
            }
            EditorApplication.update -= Tick;
            File.Delete(RequestPath);
            Debug.Log("Frosty Neon: starting the requested " + request.platform + " upload of " + request.prefab);
            AvatarUpload.Upload(request.prefab, request.name, request.platform == "quest");
        }

        /// <summary>Stops a request without uploading, and tells frosty.ps1 why.</summary>
        static void Finish(string message, Request request, bool dialog = false)
        {
            EditorApplication.update -= Tick;
            if (File.Exists(RequestPath)) File.Delete(RequestPath);
            var result = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-upload-request",
                ["uploaded"] = false,
                ["prefab"] = request?.prefab,
                ["platform"] = request?.platform,
                ["message"] = message,
            };
            AvatarPreflight.WriteReport(result, ResultPath);
            Debug.LogWarning("Frosty Neon upload request: " + message);
            if (dialog) EditorUtility.DisplayDialog("Frosty Neon upload", message, "OK");
        }
    }
}
