// Self-test for AvatarSetupHelper (Editor-only).
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarSetupHelperSelfTest.Run -logFile <log>
//   Exit code 0 = all checks passed, 1 = a check failed. Results: Logs/FrostyNeon/avatar_setup_helper_selftest.json.
//
// It works only on copies, which it deletes again. The existing fixtures and their .meta files are
// hashed before and after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarSetupHelperSelfTest
    {
        const string Fixtures = "Assets/FrostyNeon/Fixtures/";
        const string Basic = Fixtures + "fbx_fixture_v1.fbx";
        const string Humanoid = Fixtures + "fbx_humanoid_fixture_v1.fbx";
        const string ExplicitChest = Fixtures + "fbx_humanoid_fixture_v1_explicit_chest.fbx";
        const string Ready = Fixtures + "fbx_humanoid_fixture_v1_ready.fbx";
        const string CopyA = Fixtures + "_selftest_setup_copy_a.fbx";
        const string CopyB = Fixtures + "_selftest_setup_copy_b.fbx";
        const string OutPath = "Logs/FrostyNeon/avatar_setup_helper_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon setup helper self-test crashed: " + error); }
            finally { AssetDatabase.DeleteAsset(CopyA); AssetDatabase.DeleteAsset(CopyB); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            AssetDatabase.DeleteAsset(CopyA);
            AssetDatabase.DeleteAsset(CopyB);
            string[] hashed = { Basic, Humanoid, ExplicitChest, Ready };
            Dictionary<string, string> before = Hashes(hashed);

            // 1. Dry run on the automatically mapped humanoid fixture.
            Dictionary<string, object> dry = AvatarSetupHelper.Execute(Humanoid, AvatarSetupHelper.Mode.DryRun);
            Check("dry run: reports Read/Write and Chest as changes that would be made, predicts PC pass (Quest shader remains)",
                  (string)dry["mode"] == "dry_run" && !(bool)dry["settings_changed"] &&
                  Changes(dry, "changes_that_would_be_made") == "Humanoid bone mapping: Chest: (empty) -> Chest; Legacy Blendshape Normals: disabled -> enabled; Mesh Read/Write: disabled -> enabled" &&
                  AfterPc(dry) == "pass" && AfterCodes(dry) == "sdk_quest_unsupported_shader" && BeforeStatus(dry) == "fail",
                  Changes(dry, "changes_that_would_be_made") + " | " + dry["status"]);
            Check("dry run: source import settings unchanged, temporary copy removed",
                  Same(before, Hashes(hashed)) && !AssetDatabase.IsValidFolder(AvatarSetupHelper.DryRunFolder) &&
                  !Directory.Exists(AvatarSetupHelper.DryRunFolder), "");

            // 2. Dry run on a rig Unity cannot make humanoid (four bones): reported, nothing guessed.
            Dictionary<string, object> basic = AvatarSetupHelper.Execute(Basic, AvatarSetupHelper.Mode.DryRun);
            Check("dry run on the four-bone figure: reports that no humanoid Avatar can be built; status fail",
                  (string)basic["status"] == "fail" && Problems(basic) == "humanoid_avatar_invalid" && Same(before, Hashes(hashed)),
                  Problems(basic) + " | " + basic["status"]);

            // 3. Apply to a copy.
            Dictionary<string, object> applied = AvatarSetupHelper.Execute(Humanoid, AvatarSetupHelper.Mode.ApplyToCopy, CopyA);
            var after = (Dictionary<string, object>)applied["preflight_after"];
            Check("apply to copy: changes listed, copy passes for PC with 21 slots mapped; only the Quest shader finding remains",
                  (bool)applied["settings_changed"] && (string)applied["target_asset"] == CopyA &&
                  Changes(applied, "changes_made") == "Humanoid bone mapping: Chest: (empty) -> Chest; Legacy Blendshape Normals: disabled -> enabled; Mesh Read/Write: disabled -> enabled" &&
                  AfterPc(applied) == "pass" && AfterCodes(applied) == "sdk_quest_unsupported_shader" &&
                  (int)((Dictionary<string, object>)after["rig"])["mapped_slot_count"] == 21,
                  Changes(applied, "changes_made") + " | " + applied["status"]);
            var importer = (ModelImporter)AssetImporter.GetAtPath(CopyA);
            Check("apply to copy: the copy's import settings really hold the changes",
                  importer.isReadable && importer.animationType == ModelImporterAnimationType.Human &&
                  importer.humanDescription.human.Any(b => b.humanName == "Chest" && b.boneName == "Chest"), "");
            Check("apply to copy: original untouched", Same(before, Hashes(hashed)), "");

            // 4. Apply in place, on that copy: already set up, so nothing more to change.
            Dictionary<string, object> again = AvatarSetupHelper.Execute(CopyA, AvatarSetupHelper.Mode.ApplyInPlace);
            Check("apply in place on an already prepared model: no changes, PC still pass",
                  (string)again["mode"] == "applied_in_place" && Changes(again, "changes_made") == "" && AfterPc(again) == "pass",
                  Changes(again, "changes_made") + " | " + again["status"]);

            // 5. Apply in place starting from a Generic import of the humanoid rig.
            AssetDatabase.CopyAsset(Humanoid, CopyB);
            var generic = (ModelImporter)AssetImporter.GetAtPath(CopyB);
            generic.animationType = ModelImporterAnimationType.Generic;
            generic.humanDescription = new HumanDescription();
            generic.SaveAndReimport();
            Dictionary<string, object> inPlace = AvatarSetupHelper.Execute(CopyB, AvatarSetupHelper.Mode.ApplyInPlace);
            Check("apply in place from a Generic import: Animation Type, Read/Write and Chest changed; PC pass",
                  Changes(inPlace, "changes_made") == "Animation Type: Generic -> Human; Humanoid bone mapping: Chest: (empty) -> Chest; Legacy Blendshape Normals: disabled -> enabled; Mesh Read/Write: disabled -> enabled" &&
                  AfterPc(inPlace) == "pass" && AfterCodes(inPlace) == "sdk_quest_unsupported_shader",
                  Changes(inPlace, "changes_made") + " | " + AfterPc(inPlace) + " | " + AfterCodes(inPlace));

            // 5a. A stored humanoid skeleton 100 times the model (the bug seen in VRChat on 2026-11-15 with
            // centimetre files). Blender cannot produce such a file, so the mismatch is written into a copy directly.
            const string Mismatch = Fixtures + "_selftest_setup_mismatch.fbx";
            AssetDatabase.DeleteAsset(Mismatch);
            AssetDatabase.CopyAsset(Ready, Mismatch);
            var mmImporter = (ModelImporter)AssetImporter.GetAtPath(Mismatch);
            HumanDescription scaled = AssetDatabase.LoadAllAssetsAtPath(Mismatch).OfType<Avatar>().First().humanDescription;
            scaled.skeleton = scaled.skeleton.Select(b => { b.position *= 100f; return b; }).ToArray();
            mmImporter.humanDescription = scaled;
            mmImporter.SaveAndReimport();
            try
            {
                float skeletonBefore = AvatarSetupHelper.StoredSkeletonScale(Mismatch);
                Dictionary<string, object> mmBefore = AvatarPreflight.AnalyzeAsset(Mismatch, null);
                Check("stored skeleton 100 times the model: measured and reported by the preflight as an error",
                      Mathf.Abs(skeletonBefore - 100f) < 1f && Codes(mmBefore).Contains("humanoid_rest_pose_scale_mismatch"),
                      "stored x" + skeletonBefore + " | " + string.Join(",", Codes(mmBefore)));
                Dictionary<string, object> mmFixed = AvatarSetupHelper.Execute(Mismatch, AvatarSetupHelper.Mode.ApplyInPlace);
                float skeletonAfter = AvatarSetupHelper.StoredSkeletonScale(Mismatch);
                Check("setup helper rebuilds the skeleton record: it matches the model again and the preflight passes for PC",
                      Mathf.Abs(skeletonAfter - 1f) < 0.01f && Changes(mmFixed, "changes_made").Contains("Humanoid skeleton record") &&
                      AfterPc(mmFixed) == "pass" && !AfterCodes(mmFixed).Contains("humanoid_rest_pose_scale_mismatch"),
                      "stored x" + skeletonAfter + " | " + Changes(mmFixed, "changes_made"));
            }
            finally { AssetDatabase.DeleteAsset(Mismatch); }

            // 5b. A model imported 100 times too small is scaled back.
            const string Tiny = Fixtures + "_selftest_setup_tiny.fbx";
            AssetDatabase.DeleteAsset(Tiny);
            AssetDatabase.CopyAsset(Humanoid, Tiny);
            var tinyImporter = (ModelImporter)AssetImporter.GetAtPath(Tiny);
            tinyImporter.globalScale = 0.01f;
            tinyImporter.SaveAndReimport();
            try
            {
                Dictionary<string, object> tiny = AvatarSetupHelper.Execute(Tiny, AvatarSetupHelper.Mode.ApplyInPlace);
                float height = AvatarSetupHelper.ModelHeight(Tiny);
                Check("a model 100 times too small is scaled by 100 and passes for PC",
                      Changes(tiny, "changes_made").Contains("Scale Factor") && Mathf.Abs(height - 1.71f) < 0.05f && AfterPc(tiny) == "pass",
                      height + " m | " + Changes(tiny, "changes_made"));
            }
            finally { AssetDatabase.DeleteAsset(Tiny); }

            // 6. Refusals.
            bool refused = false;
            try { AvatarSetupHelper.Execute(Humanoid, AvatarSetupHelper.Mode.ApplyToCopy, CopyA); } catch (IOException) { refused = true; }
            Check("existing copy target is not overwritten", refused, "");
            refused = false;
            try { AvatarSetupHelper.Execute("Assets/Editor/FrostyNeon/AvatarSetupHelper.cs", AvatarSetupHelper.Mode.DryRun); }
            catch (FileNotFoundException) { refused = true; }
            Check("a file that is not a model is refused", refused, "");

            // 7. Chest candidate rule, on bones built in memory.
            var root = new GameObject("CandidateTest");
            try
            {
                Transform hips = Bone("Hips", root.transform), spine = Bone("Spine", hips), chest = Bone("Chest", spine);
                Transform neck = Bone("Neck", chest), left = Bone("L", chest), right = Bone("R", chest);
                var used = new HashSet<Transform> { hips, spine, neck, left, right };
                Check("chest candidate: shared parent of neck and shoulders is chosen",
                      AvatarSetupHelper.FindChestCandidate(neck, left, right, spine, used, out string why) == chest, why);
                right.SetParent(spine, false);
                Check("chest candidate: shoulders under different parents is ambiguous, nothing chosen",
                      AvatarSetupHelper.FindChestCandidate(neck, left, right, spine, used, out why) == null && why.Contains("ambiguous"), why);
                neck.SetParent(spine, false); left.SetParent(spine, false);
                Check("chest candidate: parent already used as Spine, nothing chosen",
                      AvatarSetupHelper.FindChestCandidate(neck, left, right, spine, used, out why) == null && why.Contains("already used"), why);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }

            // 8. Clean up and confirm nothing is left behind.
            AssetDatabase.DeleteAsset(CopyA);
            AssetDatabase.DeleteAsset(CopyB);
            Check("fixtures unchanged and no test copies left in the project",
                  Same(before, Hashes(hashed)) && !File.Exists(CopyA) && !File.Exists(CopyB) && !File.Exists(CopyA + ".meta") &&
                  !Directory.Exists(AvatarSetupHelper.DryRunFolder) && !File.Exists(AvatarSetupHelper.DryRunFolder + ".meta"), "");

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-setup-helper-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon setup helper self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        static Transform Bone(string name, Transform parent)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            return bone;
        }

        static HashSet<string> Codes(Dictionary<string, object> report)
        {
            return new HashSet<string>(((List<object>)report["findings"]).Cast<Dictionary<string, object>>().Select(f => (string)f["code"]));
        }

        static string Changes(Dictionary<string, object> report, string key)
        {
            return string.Join("; ", ((List<object>)report[key]).Cast<Dictionary<string, object>>()
                .Select(c => c["setting"] + ": " + c["before"] + " -> " + c["after"]).OrderBy(c => c, StringComparer.Ordinal));
        }

        static string Problems(Dictionary<string, object> report)
        {
            return string.Join(",", ((List<object>)report["problems"]).Cast<Dictionary<string, object>>().Select(p => (string)p["code"]));
        }

        static string AfterPc(Dictionary<string, object> report)
        {
            var after = (Dictionary<string, object>)report["preflight_after"];
            return (string)((Dictionary<string, object>)((Dictionary<string, object>)after["platforms"])["pc"])["status"];
        }

        static string AfterCodes(Dictionary<string, object> report)
        {
            var after = (Dictionary<string, object>)report["preflight_after"];
            return string.Join(",", ((List<object>)after["findings"]).Cast<Dictionary<string, object>>()
                .Select(f => (string)f["code"]).OrderBy(c => c, StringComparer.Ordinal));
        }

        static string BeforeStatus(Dictionary<string, object> report)
        {
            return (string)((Dictionary<string, object>)report["preflight_before"])["status"];
        }

        static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && b[p.Key] == p.Value);
        }

        static Dictionary<string, string> Hashes(IEnumerable<string> assets)
        {
            var result = new Dictionary<string, string>();
            using (SHA256 sha = SHA256.Create())
                foreach (string asset in assets)
                    foreach (string file in new[] { asset, asset + ".meta" })
                        result[file] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
            return result;
        }

        static void Check(string name, bool ok, string detail)
        {
            Results.Add(new Dictionary<string, object> { ["check"] = name, ["ok"] = ok, ["detail"] = detail });
            if (!ok) _failed = true;
            Debug.Log("  [" + (ok ? "ok" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
        }
    }
}
