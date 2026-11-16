// Self-test for the Guided Fix (AvatarGuidedFix): a throwaway "ready-made" avatar with the three import-setting
// problems that stop a VRChat upload is fixed, its files are checked to be untouched, and the fix is undone.
// Batch mode, uploads nothing; everything it creates is removed. Run:
//   Unity.exe -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarGuidedFixSelfTest.Run -logFile <log>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarGuidedFixSelfTest
    {
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string Robot = "Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab";
        const string TempFolder = "Assets/FrostyNeon/_GuidedFixSelfTest";
        const string Model = TempFolder + "/ShopBody.fbx";
        const string Texture = TempFolder + "/ShopSkin.png";
        const string MaterialPath = TempFolder + "/ShopSkin.mat";
        const string Prefab = TempFolder + "/Shop_Avatar.prefab";
        const string OutPath = "Logs/FrostyNeon/avatar_guided_fix_selftest.json";
        static readonly string[] Codes = { "mesh_read_write_disabled", "sdk_legacy_blend_shape_normals", "sdk_texture_streaming_mipmaps" };

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon guided fix self-test crashed: " + error); }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
                if (File.Exists(AvatarGuidedFix.LogPath(Prefab))) File.Delete(AvatarGuidedFix.LogPath(Prefab));
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_GuidedFixSelfTest");
            BuildBrokenAvatar();

            // 1. The check sees the three problems, and the Guide files them under "fixed for you" for a prefab.
            Dictionary<string, object> before = AvatarPreflight.AnalyzeAsset(Prefab, null);
            List<string> beforeCodes = CodesIn(before);
            Check("the broken ready-made avatar shows all three upload-blocking import problems; PC fails",
                  Codes.All(beforeCodes.Contains) && PcStatus(before) == "fail", string.Join(",", beforeCodes));
            List<Dictionary<string, object>> fixedForYou = FindingText.Sort(before, false).First(g => g.Key == FindingText.FixedForYou).Value;
            Check("the Guide lists them under 'fixed for you' for a ready-made prefab",
                  Codes.All(c => fixedForYou.Any(f => (string)f["code"] == c)), fixedForYou.Count + " in the group");

            // 2. The plan names exactly those settings.
            List<AvatarGuidedFix.Change> plan = AvatarGuidedFix.Plan(Prefab);
            Check("the plan is three import-setting changes: Read/Write and Legacy Blendshape Normals on the model, Streaming Mip Maps on the texture",
                  plan.Count == 3 && plan.Any(c => c.setting == AvatarGuidedFix.ReadWrite && c.asset == Model) &&
                  plan.Any(c => c.setting == AvatarGuidedFix.LegacyNormals && c.asset == Model) &&
                  plan.Any(c => c.setting == AvatarGuidedFix.StreamingMips && c.asset == Texture),
                  string.Join(" | ", plan.Select(c => c.ToString())));
            Check("a working avatar (FrostyNeonBot) needs no changes", AvatarGuidedFix.Plan(Robot).Count == 0, "");

            // 3. Apply: the problems are gone and the creator's files are byte-for-byte the same.
            var files = new[] { Prefab, MaterialPath, Model, Texture };
            Dictionary<string, string> hashes = files.ToDictionary(f => f, Hash);
            AvatarGuidedFix.Log log = AvatarGuidedFix.Apply(Prefab);
            Dictionary<string, object> after = AvatarPreflight.AnalyzeAsset(Prefab, null);
            Check("after the fix none of the three problems is reported and PC no longer fails",
                  !Codes.Any(CodesIn(after).Contains) && PcStatus(after) != "fail" && log.status_before == "fail",
                  string.Join(",", CodesIn(after)) + " | pc " + PcStatus(after));
            Check("the prefab, material, model and texture files are unchanged (only Unity's import settings changed)",
                  files.All(f => Hash(f) == hashes[f]), string.Join(", ", files.Where(f => Hash(f) != hashes[f])));
            AvatarGuidedFix.Log saved = AvatarGuidedFix.ReadLog(Prefab);
            Check("every change is recorded with its old value, none failed",
                  saved != null && saved.changes.Count == 3 && saved.changes.All(c => c.Made && !c.before && c.after) && AvatarGuidedFix.CanUndo(Prefab), "");
            Check("running the plan again finds nothing left to fix", AvatarGuidedFix.Plan(Prefab).Count == 0, "");
            string twin = Path.GetDirectoryName(Prefab).Replace('\\', '/') + "/Other/" + Path.GetFileName(Prefab);
            Check("another avatar with the same file name in another folder has its own undo record",
                  AvatarGuidedFix.LogPath(twin) != AvatarGuidedFix.LogPath(Prefab) && !AvatarGuidedFix.CanUndo(twin), AvatarGuidedFix.LogPath(twin));

            // 4. Undo puts every setting back and the check reports the problems again.
            int restored = AvatarGuidedFix.Undo(Prefab);
            var model = (ModelImporter)AssetImporter.GetAtPath(Model);
            var texture = (TextureImporter)AssetImporter.GetAtPath(Texture);
            Check("undo restores the three settings and removes the record",
                  restored == 3 && !model.isReadable && AvatarPreflight.NeedsLegacyBlendShapeNormals(model) && !texture.streamingMipmaps &&
                  !AvatarGuidedFix.CanUndo(Prefab) && Codes.All(CodesIn(AvatarPreflight.AnalyzeAsset(Prefab, null)).Contains), "restored " + restored);

            // 5. Fixing in two goes keeps the whole history, so one Undo still restores everything.
            model.isReadable = true;
            model.SaveAndReimport();
            AvatarGuidedFix.Apply(Prefab);             // fixes the other two
            model.isReadable = false;
            model.SaveAndReimport();
            AvatarGuidedFix.Log second = AvatarGuidedFix.Apply(Prefab);   // fixes Read/Write again
            int restoredAll = AvatarGuidedFix.Undo(Prefab);
            texture = (TextureImporter)AssetImporter.GetAtPath(Texture);
            Check("fixing in two goes is recorded in full; one undo restores the settings from before the first fix",
                  second.changes.Count == 3 && restoredAll == 3 && !texture.streamingMipmaps &&
                  AvatarPreflight.NeedsLegacyBlendShapeNormals((ModelImporter)AssetImporter.GetAtPath(Model)),
                  "recorded " + second.changes.Count + ", restored " + restoredAll);

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-guided-fix-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon guided fix self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        /// <summary>An avatar prefab like a shop's: descriptor set up, a textured material, and import settings VRChat rejects.</summary>
        static void BuildBrokenAvatar()
        {
            AssetDatabase.CopyAsset(Ready, Model);
            var model = (ModelImporter)AssetImporter.GetAtPath(Model);
            model.isReadable = false;
            model.importBlendShapeNormals = ModelImporterNormals.Calculate;
            AvatarPreflight.SetLegacyBlendShapeNormals(model, false);
            model.SaveAndReimport();

            var pixels = new Texture2D(64, 64);
            File.WriteAllBytes(Texture, pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(Texture, ImportAssetOptions.ForceSynchronousImport);
            var texture = (TextureImporter)AssetImporter.GetAtPath(Texture);
            texture.mipmapEnabled = true;
            texture.streamingMipmaps = false;
            texture.SaveAndReimport();

            var material = new Material(Shader.Find("Standard")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Texture) };
            AssetDatabase.CreateAsset(material, MaterialPath);

            AvatarDescriptorSetup.Execute(Model, Prefab, true);
            GameObject contents = PrefabUtility.LoadPrefabContents(Prefab);
            foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            PrefabUtility.SaveAsPrefabAsset(contents, Prefab);
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.SaveAssets();
        }

        static List<string> CodesIn(Dictionary<string, object> report) =>
            ((List<object>)report["findings"]).Cast<Dictionary<string, object>>().Select(f => (string)f["code"]).ToList();

        static string PcStatus(Dictionary<string, object> report) =>
            (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])["pc"])["status"];

        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        static void Check(string name, bool ok, string detail)
        {
            Results.Add(new Dictionary<string, object> { ["check"] = name, ["ok"] = ok, ["detail"] = detail });
            if (!ok) _failed = true;
            Debug.Log("  [" + (ok ? "ok" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
        }
    }
}
