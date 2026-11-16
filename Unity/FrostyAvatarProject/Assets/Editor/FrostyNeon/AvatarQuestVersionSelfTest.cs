// Self-test for Quest versions (AvatarQuestVersion): a throwaway "ready-made" avatar with a PC material and a pink
// (shader not installed, see-through) material gets a Quest version; the creator's files are checked to be untouched,
// the avatar ID is shared, and the Quest version is rebuilt and removed. Batch mode, uploads nothing; everything it
// creates is removed. Run:
//   Unity.exe -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarQuestVersionSelfTest.Run -logFile <log>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using VRC.Core;

namespace FrostyNeon.Editor
{
    public static class AvatarQuestVersionSelfTest
    {
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string TempFolder = "Assets/FrostyNeon/_QuestVersionSelfTest";
        const string Model = TempFolder + "/ShopBody.fbx";
        const string Texture = TempFolder + "/ShopSkin.png";
        const string PcMaterial = TempFolder + "/ShopSkin.mat";
        const string PinkMaterial = TempFolder + "/ShopHair.mat";
        const string Prefab = TempFolder + "/Shop_Avatar.prefab";
        const string OutPath = "Logs/FrostyNeon/avatar_quest_version_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon Quest version self-test crashed: " + error); }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
                if (File.Exists(AvatarQuestVersion.LogPath(Prefab))) File.Delete(AvatarQuestVersion.LogPath(Prefab));
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_QuestVersionSelfTest");
            BuildShopAvatar();
            string quest = AvatarQuestVersion.QuestPathFor(Prefab);

            // 1. Before: Quest refuses both materials.
            Dictionary<string, object> before = AvatarPreflight.AnalyzeAsset(Prefab, null);
            Check("before: the avatar fails for Quest on its shaders, and both materials are refused",
                  CodesIn(before).Contains("sdk_quest_unsupported_shader") && Status(before, "quest") == "fail" &&
                  AvatarQuestVersion.RefusedMaterials(Prefab).Count == 2 && AvatarQuestVersion.QuestPrefabFor(Prefab) == null,
                  string.Join(",", CodesIn(before)));
            Check("the Guide files the Quest shader problem under 'fixed for you' for a ready-made prefab",
                  FindingText.Sort(before, false).First(g => g.Key == FindingText.FixedForYou).Value.Any(f => (string)f["code"] == "sdk_quest_unsupported_shader"), "");

            // 2. Make the Quest version.
            var files = new[] { Prefab, PcMaterial, PinkMaterial, Model, Texture };
            Dictionary<string, string> hashes = files.ToDictionary(f => f, Hash);
            AvatarQuestVersion.Result made = AvatarQuestVersion.Make(Prefab);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(quest);
            Check("a Quest version is made as a prefab variant of the avatar, in FrostyNeon_Quest beside it, and found again",
                  made.QuestPrefab == quest && asset != null && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant &&
                  AvatarQuestVersion.IsQuestVersion(quest) && AvatarQuestVersion.PcPrefabFor(quest) == Prefab &&
                  AvatarQuestVersion.QuestPrefabFor(Prefab) == quest && quest == TempFolder + "/FrostyNeon_Quest/Shop_Avatar_Quest.prefab", quest);
            Dictionary<string, object> after = AvatarPreflight.AnalyzeAsset(quest, null);
            Check("the Quest version passes the Quest check: no unsupported or missing shaders",
                  !CodesIn(after).Contains("sdk_quest_unsupported_shader") && !CodesIn(after).Contains("material_shader_missing") &&
                  Status(after, "quest") != "fail", string.Join(",", CodesIn(after)) + " | quest " + Status(after, "quest"));

            List<Material> questMaterials = Materials(quest);
            Material fromPc = questMaterials.FirstOrDefault(m => m.name == "ShopSkin_Quest");
            Material fromPink = questMaterials.FirstOrDefault(m => m.name == "ShopHair_Quest");
            var png = AssetDatabase.LoadAssetAtPath<Texture2D>(Texture);
            Check("an opaque material becomes Toon Standard with its colour and texture; a transparent one (read from the pink material's saved data) a see-through Quest shader with its texture and tiling",
                  made.Converted.Count == 2 && fromPc && fromPink && fromPc.shader.name == AvatarQuestVersion.QuestShader &&
                  fromPc.mainTexture == png && Near(fromPc.color, Color.red) &&
                  fromPink.shader.name == AvatarQuestVersion.MultiplyShader && fromPink.mainTexture == png && fromPink.mainTextureScale == new Vector2(2, 2),
                  string.Join(" | ", made.Converted));
            Check("the transparent material is reported as see-through, nothing as cut-out",
                  made.SeeThrough.SequenceEqual(new[] { "ShopHair" }) && made.CutOut.Count == 0, string.Join(",", made.SeeThrough));
            string bright = TempFolder + "/Bright.png";
            var white = new Texture2D(8, 8);
            white.SetPixels32(Enumerable.Repeat(new Color32(250, 250, 255, 255), 64).ToArray());
            File.WriteAllBytes(bright, white.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(white);
            AssetDatabase.ImportAsset(bright, ImportAssetOptions.ForceSynchronousImport);
            Check("a bright texture (a highlight) counts as mostly bright, the dark one does not, so they get Additive and Multiply",
                  AvatarQuestVersion.MostlyBright(AssetDatabase.LoadAssetAtPath<Texture2D>(bright)) && !AvatarQuestVersion.MostlyBright(png), "");
            Check("the creator's prefab, materials, model and texture files are unchanged",
                  files.All(f => Hash(f) == hashes[f]), string.Join(", ", files.Where(f => Hash(f) != hashes[f])));

            var importer = (TextureImporter)AssetImporter.GetAtPath(Texture);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            TextureImporterPlatformSettings pc = importer.GetPlatformTextureSettings("Standalone");
            Check("the texture is capped at 1024 for Quest only; the PC size stays 2048",
                  android.overridden && android.maxTextureSize == 1024 && !pc.overridden && importer.maxTextureSize == 2048 &&
                  made.Textures.Count == 1, "android " + android.overridden + "/" + android.maxTextureSize + ", pc " + importer.maxTextureSize);

            // 3. Rebuilding keeps the original texture settings on record.
            AvatarQuestVersion.Result again = AvatarQuestVersion.Make(Prefab);
            AvatarQuestVersion.Log log = AvatarQuestVersion.ReadLog(Prefab);
            Check("making it again replaces it cleanly and keeps the texture's original settings on record",
                  again.Converted.Count == 2 && Directory.GetFiles(TempFolder + "/FrostyNeon_Quest/Materials", "*.mat").Length == 2 &&
                  log != null && log.textures.Count == 1 && !log.textures[0].before_overridden, "");

            // 4. One avatar ID for both versions.
            SetId(Prefab, "avtr_selftest-pc");
            bool inherits = AvatarQuestVersion.IdOf(quest) == "avtr_selftest-pc";
            SetId(Prefab, "");
            SetId(quest, "avtr_selftest-quest");
            AvatarQuestVersion.SyncIds(quest);
            bool copiedBack = AvatarQuestVersion.IdOf(Prefab) == "avtr_selftest-quest";
            SetId(Prefab, "avtr_selftest-a");
            SetId(quest, "avtr_selftest-b");
            string conflict = AvatarQuestVersion.SyncIds(Prefab);
            Check("the Quest version shares the avatar's ID; an ID only the Quest version has is copied back; different IDs are reported, not changed",
                  inherits && copiedBack && conflict != null && AvatarQuestVersion.IdOf(Prefab) == "avtr_selftest-a" &&
                  AvatarQuestVersion.IdOf(quest) == "avtr_selftest-b", "inherits " + inherits + ", copied back " + copiedBack);
            SetId(quest, "avtr_selftest-a");

            // 5. Remove puts everything back.
            int restored = AvatarQuestVersion.Remove(Prefab);
            importer = (TextureImporter)AssetImporter.GetAtPath(Texture);
            Check("removing it deletes the Quest folder and puts the texture's Quest setting back",
                  restored == 1 && !AssetDatabase.IsValidFolder(TempFolder + "/FrostyNeon_Quest") && !importer.GetPlatformTextureSettings("Android").overridden &&
                  AvatarQuestVersion.ReadLog(Prefab) == null && AvatarQuestVersion.QuestPrefabFor(Prefab) == null, "restored " + restored);

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-quest-version-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon Quest version self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        /// <summary>An avatar prefab like a shop's: a red Standard material and a green "lilToon" material whose shader is missing.</summary>
        static void BuildShopAvatar()
        {
            AssetDatabase.CopyAsset(Ready, Model);
            var pixels = new Texture2D(2048, 2048);
            pixels.SetPixels32(Enumerable.Repeat(new Color32(50, 40, 60, 255), 2048 * 2048).ToArray());   // dark, like eyelashes
            File.WriteAllBytes(Texture, pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(Texture, ImportAssetOptions.ForceSynchronousImport);
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(Texture);
            textureImporter.streamingMipmaps = true;    // as after the Guided Fix; this test is about shaders
            textureImporter.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Texture);

            var standard = new Material(Shader.Find("Standard")) { color = Color.red, mainTexture = texture };
            AssetDatabase.CreateAsset(standard, PcMaterial);
            string guid = AssetDatabase.AssetPathToGUID(Texture);
            File.WriteAllText(PinkMaterial,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n" +
                "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
                "  m_PrefabAsset: {fileID: 0}\n  m_Name: ShopHair\n" +
                "  m_Shader: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n" +
                "  m_ValidKeywords: []\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 0\n" +
                "  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: 3000\n  stringTagMap: {}\n  disabledShaderPasses: []\n" +
                "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n    - _MainTex:\n        m_Texture: {fileID: 2800000, guid: " + guid +
                ", type: 3}\n        m_Scale: {x: 2, y: 2}\n        m_Offset: {x: 0, y: 0}\n    m_Ints: []\n" +
                "    m_Floats:\n    - _lilToonVersion: 1\n    m_Colors:\n    - _Color: {r: 0, g: 1, b: 0, a: 0.5}\n  m_BuildTextureStacks: []\n");
            AssetDatabase.ImportAsset(PinkMaterial, ImportAssetOptions.ForceSynchronousImport);
            var pink = AssetDatabase.LoadAssetAtPath<Material>(PinkMaterial);

            AvatarDescriptorSetup.Execute(Model, Prefab, true);
            GameObject contents = PrefabUtility.LoadPrefabContents(Prefab);
            Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterials = i == 0 ? new[] { standard, pink } : new[] { standard };
            PrefabUtility.SaveAsPrefabAsset(contents, Prefab);
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.SaveAssets();
        }

        static List<Material> Materials(string prefab) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();

        static void SetId(string prefab, string id)
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefab))
                scope.prefabContentsRoot.GetComponent<PipelineManager>().blueprintId = id;
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        static List<string> CodesIn(Dictionary<string, object> report) =>
            ((List<object>)report["findings"]).Cast<Dictionary<string, object>>().Select(f => (string)f["code"]).ToList();

        static string Status(Dictionary<string, object> report, string platform) =>
            (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])[platform])["status"];

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
