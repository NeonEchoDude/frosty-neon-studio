// Self-test for AvatarUploadRequest (the request file frosty.ps1 upload writes) and the per-platform readiness check.
// Batch mode, uploads nothing. Run:
//   Unity.exe -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarUploadRequestSelfTest.Run -logFile <log>
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarUploadRequestSelfTest
    {
        const string Robot = "Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab";
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string TempFolder = "Assets/FrostyNeon/_UploadRequestSelfTest";
        const string ReadyPrefab = TempFolder + "/Ready_Avatar.prefab";
        const string PlainPrefab = TempFolder + "/Plain.prefab";
        const string RequestFile = "Logs/FrostyNeon/_selftest_upload_request.json";
        const string OutPath = "Logs/FrostyNeon/avatar_upload_request_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon upload request self-test crashed: " + error); }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
                if (File.Exists(RequestFile)) File.Delete(RequestFile);
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_UploadRequestSelfTest");
            DateTime now = DateTime.UtcNow;
            string robotBefore = File.ReadAllText(Robot);

            Expect("valid PC request for the starter avatar is accepted", Request(Robot, "FrostyNeonBot", "pc", now), now, null);
            Expect("valid Quest request is accepted", Request(Robot, "FrostyNeonBot", "quest", now.AddMinutes(-29)), now, null);
            Expect("request older than 30 minutes is refused", Request(Robot, "FrostyNeonBot", "pc", now.AddMinutes(-31)), now, "not within the last 30 minutes");
            Expect("request dated in the future is refused", Request(Robot, "FrostyNeonBot", "pc", now.AddMinutes(10)), now, "not within the last 30 minutes");
            Expect("unknown platform is refused", Request(Robot, "FrostyNeonBot", "xbox", now), now, "Unknown platform");
            Expect("missing avatar name is refused", Request(Robot, " ", "pc", now), now, "no avatar name");
            Expect("missing prefab is refused", Request("Assets/Nope_Avatar.prefab", "Nope", "pc", now), now, "No avatar prefab");
            var plain = new GameObject("Plain");
            PrefabUtility.SaveAsPrefabAsset(plain, PlainPrefab);
            UnityEngine.Object.DestroyImmediate(plain);
            Expect("prefab without an avatar descriptor is refused", Request(PlainPrefab, "Plain", "pc", now), now, "No avatar prefab");
            File.WriteAllText(RequestFile, "{ not json");
            AvatarUploadRequest.Request broken = AvatarUploadRequest.Read(RequestFile, now, out string brokenProblem);
            Check("unreadable request file is refused", broken == null && brokenProblem != null, brokenProblem);
            Check("build targets: pc -> StandaloneWindows64, quest -> Android",
                  AvatarUploadRequest.TargetFor("pc") == BuildTarget.StandaloneWindows64 && AvatarUploadRequest.TargetFor("quest") == BuildTarget.Android, "");

            // Per-platform readiness: an avatar that passes on PC but fails on Quest (Standard shader) must not be
            // blocked for PC. Before 2026-11-15 the uploader used the overall status and would have refused it.
            AvatarDescriptorSetup.Execute(Ready, ReadyPrefab, true);
            Dictionary<string, object> ready = AvatarUpload.Readiness(ReadyPrefab);
            Check("readiness reports PC and Quest separately: ready fixture PC pass, Quest fail (Standard shader)",
                  (bool)ready["prefab_ok"] && (string)ready["pc_status"] == "pass" && (string)ready["quest_status"] == "fail" &&
                  (string)ready["preflight_status"] == "fail",
                  ready["pc_status"] + " / " + ready["quest_status"] + " (overall " + ready["preflight_status"] + ")");
            Dictionary<string, object> robot = AvatarUpload.Readiness(Robot);
            Check("readiness for the starter avatar: pass on both, avatar ID present",
                  (string)robot["pc_status"] == "pass" && (string)robot["quest_status"] == "pass" && (bool)robot["avatar_id_already_set"],
                  robot["pc_status"] + " / " + robot["quest_status"]);
            Check("the starter avatar prefab was not changed", File.ReadAllText(Robot) == robotBefore, "");

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-upload-request-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon upload request self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        static string Request(string prefab, string name, string platform, DateTime when)
        {
            var request = new AvatarUploadRequest.Request { prefab = prefab, name = name, platform = platform, requested_at = when.ToString("o") };
            File.WriteAllText(RequestFile, JsonUtility.ToJson(request));
            return RequestFile;
        }

        static void Expect(string name, string file, DateTime now, string problemContains)
        {
            AvatarUploadRequest.Request request = AvatarUploadRequest.Read(file, now, out string problem);
            bool ok = problemContains == null ? request != null && problem == null
                                              : request == null && problem != null && problem.Contains(problemContains);
            Check(name, ok, problem ?? "accepted");
        }

        static void Check(string name, bool ok, string detail)
        {
            Results.Add(new Dictionary<string, object> { ["check"] = name, ["ok"] = ok, ["detail"] = detail });
            if (!ok) _failed = true;
            Debug.Log("  [" + (ok ? "ok" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
        }
    }
}
