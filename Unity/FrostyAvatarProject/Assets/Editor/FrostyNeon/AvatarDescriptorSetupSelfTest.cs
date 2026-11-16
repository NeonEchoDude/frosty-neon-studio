// Self-test for AvatarDescriptorSetup (Editor-only).
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarDescriptorSetupSelfTest.Run -logFile <log>
//   Exit code 0 = all checks passed, 1 = a check failed. Results: Logs/FrostyNeon/avatar_descriptor_setup_selftest.json.
//
// It creates its prefab in a temporary folder, which it deletes again. Fixtures are hashed before and after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace FrostyNeon.Editor
{
    public static class AvatarDescriptorSetupSelfTest
    {
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string Basic = "Assets/FrostyNeon/Fixtures/fbx_fixture_v1.fbx";
        const string TempFolder = "Assets/FrostyNeon/_DescriptorSelfTest";
        const string Prefab = TempFolder + "/Ready_Avatar.prefab";
        const string OutPath = "Logs/FrostyNeon/avatar_descriptor_setup_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon descriptor setup self-test crashed: " + error); }
            finally { AssetDatabase.DeleteAsset(TempFolder); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            string before = Hash(Ready) + Hash(Ready + ".meta") + Hash(Basic) + Hash(Basic + ".meta");
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_DescriptorSelfTest");

            Dictionary<string, object> dry = AvatarDescriptorSetup.Execute(Ready, Prefab, false);
            var view = ((List<object>)dry["view_position"]).Select(Convert.ToSingle).ToArray();
            // Fixture head: bone from 1.50 to 1.70 m, 0.06 m wide box, nose reaching 0.10 m forward (Unity +Z).
            Check("dry run: no prefab created; view position estimated inside the head, at its front",
                  (string)dry["mode"] == "dry_run" && !(bool)dry["prefab_created"] && !File.Exists(Prefab) &&
                  ((string)dry["view_position_source"]).StartsWith("estimated from the head mesh") &&
                  Mathf.Abs(view[0]) < 0.001f && view[1] > 1.5f && view[1] < 1.7f && Mathf.Abs(view[2] - 0.10f) < 0.005f,
                  string.Join(", ", view) + " | " + dry["view_position_source"]);

            Dictionary<string, object> applied = AvatarDescriptorSetup.Execute(Ready, Prefab, true);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var descriptor = prefab ? prefab.GetComponent<VRCAvatarDescriptor>() : null;
            var pipeline = prefab ? prefab.GetComponent<PipelineManager>() : null;
            Check("apply: prefab created with an avatar descriptor and a pipeline manager with no blueprint ID",
                  (string)applied["mode"] == "applied" && (bool)applied["prefab_created"] && descriptor != null && pipeline != null &&
                  string.IsNullOrEmpty(pipeline.blueprintId),
                  descriptor != null ? "descriptor ok, blueprint '" + (pipeline != null ? pipeline.blueprintId : "?") + "'" : "no descriptor");
            Check("apply: descriptor view position equals the reported one",
                  descriptor != null && (descriptor.ViewPosition - new Vector3(view[0], view[1], view[2])).magnitude < 0.001f,
                  descriptor != null ? descriptor.ViewPosition.ToString("F4") : "");
            Check("apply: the prefab is linked to the model, not a detached copy",
                  prefab != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab) ==
                  AssetDatabase.LoadAssetAtPath<GameObject>(Ready), "");

            var preflight = (Dictionary<string, object>)applied["preflight_of_prefab"];
            var section = preflight.ContainsKey("avatar_descriptor") ? (Dictionary<string, object>)preflight["avatar_descriptor"] : null;
            var pc = (Dictionary<string, object>)((Dictionary<string, object>)preflight["platforms"])["pc"];
            Check("preflight of the prefab: PC pass, humanoid with 21 slots, descriptor reported",
                  (string)pc["status"] == "pass" && (int)((Dictionary<string, object>)preflight["rig"])["mapped_slot_count"] == 21 &&
                  section != null && (bool)section["present"] && (bool)section["pipeline_manager"] && !(bool)section["blueprint_id_set"],
                  pc["status"] + " | " + (section != null ? AvatarPreflight.ToJson(section).Replace("\n", " ") : "no avatar_descriptor section"));
            var model = AvatarPreflight.AnalyzeAsset(Ready, null);
            Check("preflight of the bare model reports no descriptor",
                  !(bool)((Dictionary<string, object>)model["avatar_descriptor"])["present"], "");

            prefab.GetComponent<VRCAvatarDescriptor>().ViewPosition = new Vector3(0f, 9f, 0f);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            Dictionary<string, object> again = AvatarDescriptorSetup.Execute(Ready, Prefab, true);
            Check("apply again: the existing prefab is left as it is",
                  (string)again["mode"] == "nothing_to_do" && !(bool)again["prefab_created"] &&
                  Mathf.Approximately(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<VRCAvatarDescriptor>().ViewPosition.y, 9f),
                  (string)again["mode"]);

            // Visemes, on skinned meshes built in memory.
            var visemeNames = Enumerable.Range(0, (int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count)
                .Select(v => ((VRC.SDKBase.VRC_AvatarDescriptor.Viseme)v).ToString().ToLowerInvariant()).ToList();
            var root = new GameObject("VisemeTest");
            var made = new List<UnityEngine.Object> { root };
            try
            {
                SkinnedMeshRenderer full = FaceMesh(root, "Face", visemeNames.Select(v => "vrc.v_" + v), made);
                FaceMesh(root, "Body", new[] { "vrc.v_aa", "Blink" }, made);
                AvatarDescriptorSetup.LipSyncMatch match = AvatarDescriptorSetup.FindVisemes(root);
                var visemeDescriptor = root.AddComponent<VRCAvatarDescriptor>();
                bool wired = AvatarDescriptorSetup.ApplyLipSync(visemeDescriptor, match);
                Check("visemes: all 15 vrc.v_ shapes found on the face mesh and wired in the SDK's order",
                      match.Complete && match.Mesh == full && wired &&
                      visemeDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                      visemeDescriptor.VisemeSkinnedMesh == full && visemeDescriptor.VisemeBlendShapes.SequenceEqual(visemeNames.Select(v => "vrc.v_" + v)),
                      match.Mesh ? match.Mesh.name + " " + string.Join(",", match.Missing) : "no mesh");

                var other = new GameObject("VisemeTest2");
                made.Add(other);
                FaceMesh(other, "Face", visemeNames.Select(v => "V_" + v.ToUpperInvariant()), made);
                var ee = new GameObject("VisemeTestEE");
                made.Add(ee);
                FaceMesh(ee, "Face", visemeNames.Select(v => v == "e" ? "vrc.v_ee" : "vrc.v_" + v), made);
                Check("visemes: vrc.v_ee is accepted for the E viseme", AvatarDescriptorSetup.FindVisemes(ee).Complete, "");
                Check("visemes: the v_ spelling is accepted, case-insensitively",
                      AvatarDescriptorSetup.FindVisemes(other).Complete, "");

                var partial = new GameObject("VisemeTest3");
                made.Add(partial);
                FaceMesh(partial, "Face", visemeNames.Skip(2).Select(v => "vrc.v_" + v), made);
                var partialMatch = AvatarDescriptorSetup.FindVisemes(partial);
                var partialDescriptor = partial.AddComponent<VRCAvatarDescriptor>();
                var lipSyncBefore = partialDescriptor.lipSync;
                Check("visemes: with two missing, lip sync is left alone and the missing shapes are named",
                      !partialMatch.Complete && !AvatarDescriptorSetup.ApplyLipSync(partialDescriptor, partialMatch) &&
                      partialDescriptor.lipSync == lipSyncBefore && partialMatch.Missing.SequenceEqual(new[] { "vrc.v_sil", "vrc.v_pp" }),
                      string.Join(",", partialMatch.Missing));
                Check("visemes: the fixture with only vrc.v_aa is reported, not wired",
                      !(bool)((Dictionary<string, object>)dry["lip_sync"])["will_wire_lip_sync"] &&
                      (int)((Dictionary<string, object>)dry["lip_sync"])["visemes_found"] == 1,
                      AvatarPreflight.ToJson(dry["lip_sync"]).Replace("\n", " "));
            }
            finally
            {
                foreach (UnityEngine.Object item in made) if (item) UnityEngine.Object.DestroyImmediate(item);
            }

            bool refused = false;
            try { AvatarDescriptorSetup.Execute(Basic, TempFolder + "/Basic_Avatar.prefab", false); }
            catch (InvalidOperationException) { refused = true; }
            Check("a model that is not humanoid is refused", refused, "");

            AssetDatabase.DeleteAsset(TempFolder);
            Check("fixtures unchanged and the temporary folder removed",
                  Hash(Ready) + Hash(Ready + ".meta") + Hash(Basic) + Hash(Basic + ".meta") == before &&
                  !Directory.Exists(TempFolder) && !File.Exists(TempFolder + ".meta"), "");

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-descriptor-setup-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon descriptor setup self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        static SkinnedMeshRenderer FaceMesh(GameObject root, string name, IEnumerable<string> shapes, List<UnityEngine.Object> made)
        {
            var mesh = new Mesh { name = name, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            foreach (string shape in shapes)
                mesh.AddBlendShapeFrame(shape, 100f, new[] { Vector3.zero, Vector3.zero, Vector3.forward * 0.01f }, null, null);
            made.Add(mesh);
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        static string Hash(string file)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
        }

        static void Check(string name, bool ok, string detail)
        {
            Results.Add(new Dictionary<string, object> { ["check"] = name, ["ok"] = ok, ["detail"] = detail });
            if (!ok) _failed = true;
            Debug.Log("  [" + (ok ? "ok" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
        }
    }
}
