// Self-test for AvatarMaterialSetup (Editor-only).
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarMaterialSetupSelfTest.Run -logFile <log>
//   Exit code 0 = all checks passed, 1 = a check failed. Results: Logs/FrostyNeon/avatar_material_setup_selftest.json.
//
// It works on a copy of the ready fixture in its own temporary folder, which it deletes again.
// The existing fixtures and their .meta files are hashed before and after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarMaterialSetupSelfTest
    {
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string TempFolder = "Assets/FrostyNeon/_MaterialSelfTest";
        const string Copy = TempFolder + "/copy.fbx";
        const string MaterialPath = TempFolder + "/Materials/FixtureSkin.mat";
        const string OutPath = "Logs/FrostyNeon/avatar_material_setup_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon material setup self-test crashed: " + error); }
            finally { AssetDatabase.DeleteAsset(TempFolder); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            string before = Hash(Ready) + Hash(Ready + ".meta");
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_MaterialSelfTest");
            AssetDatabase.CopyAsset(Ready, Copy);
            string copyMeta = Hash(Copy + ".meta");

            Dictionary<string, object> dry = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, false);
            Check("dry run: plans one material, changes nothing, Quest still fails on the Standard shader",
                  (string)dry["mode"] == "dry_run" && !(bool)dry["settings_changed"] && Plan(dry, "materials_that_would_be_set_up") ==
                  "FixtureSkin: Standard -> create with shader VRChat/Mobile/Standard Lite" &&
                  QuestStatus(dry, "preflight_now") == "fail" && Hash(Copy + ".meta") == copyMeta && !File.Exists(MaterialPath),
                  Plan(dry, "materials_that_would_be_set_up"));

            Color embedded = AssetDatabase.LoadAllAssetsAtPath(Copy).OfType<Material>().First().color;
            Dictionary<string, object> applied = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, true);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Check("apply: material asset created with the mobile shader and the embedded colour",
                  material != null && material.shader.name == "VRChat/Mobile/Standard Lite" && material.color == embedded,
                  material != null ? material.shader.name + " " + material.color : "no asset");
            var after = (Dictionary<string, object>)applied["preflight_after"];
            var detail = (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)
                ((List<object>)after["renderers"])[0])["material_details"])[0];
            Check("apply: the model now uses that asset; preflight passes on PC and Quest with no findings",
                  (string)applied["status"] == "pass" && ((List<object>)after["findings"]).Count == 0 &&
                  (string)detail["shader"] == "VRChat/Mobile/Standard Lite" && (string)detail["source"] == "material asset " + MaterialPath &&
                  QuestStatus(applied, "preflight_after") == "pass",
                  applied["status"] + " / " + detail["shader"] + " / " + detail["source"]);

            material.color = Color.magenta;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Dictionary<string, object> again = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, true);
            Check("apply again: nothing left to do, the material asset is not touched",
                  (string)again["mode"] == "nothing_to_do" && again.ContainsKey("note") && !(bool)again["settings_changed"] &&
                  Plan(again, "materials") == "" &&
                  AssetDatabase.LoadAssetAtPath<Material>(MaterialPath).color == Color.magenta && (string)again["status"] == "pass",
                  again.ContainsKey("note") ? (string)again["note"] : "no note");

            // Remove the remap so the model has an embedded material again while the asset still exists.
            var importer = (ModelImporter)AssetImporter.GetAtPath(Copy);
            importer.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "FixtureSkin"));
            importer.SaveAndReimport();
            Dictionary<string, object> reuse = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, true);
            Check("apply with a material asset already present: it is reused, not overwritten",
                  Plan(reuse, "materials") == "FixtureSkin: Standard -> reuse existing asset unchanged" &&
                  AssetDatabase.LoadAssetAtPath<Material>(MaterialPath).color == Color.magenta && (string)reuse["status"] == "pass",
                  Plan(reuse, "materials"));

            // Property copying, on materials and a texture built in memory.
            var texture = new Texture2D(2, 2);
            var source = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.4f, 0.6f, 1f), mainTexture = texture };
            source.mainTextureScale = new Vector2(2f, 3f);
            source.mainTextureOffset = new Vector2(0.25f, 0.5f);
            source.SetFloat("_Metallic", 0.3f);
            source.SetFloat("_Glossiness", 0.7f);
            var target = new Material(Shader.Find(AvatarMaterialSetup.DefaultShader));
            AvatarMaterialSetup.CopyProperties(source, target);
            Check("property copy: colour, texture with tiling and offset, metallic and smoothness carried over",
                  target.color == source.color && target.mainTexture == texture && target.mainTextureScale == new Vector2(2f, 3f) &&
                  target.mainTextureOffset == new Vector2(0.25f, 0.5f) && Mathf.Approximately(target.GetFloat("_Metallic"), 0.3f) &&
                  Mathf.Approximately(target.GetFloat("_Glossiness"), 0.7f),
                  target.mainTextureScale + " " + target.mainTextureOffset);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);

            // Texture streaming: a mipmapped texture without streaming is reported and then fixed.
            const string TexturePath = TempFolder + "/stream_test.png";
            var pixels = new Texture2D(4, 4);
            File.WriteAllBytes(TexturePath, pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(TexturePath);
            var texImporter = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            texImporter.mipmapEnabled = true;
            texImporter.streamingMipmaps = false;
            texImporter.SaveAndReimport();
            var used = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            used.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            EditorUtility.SetDirty(used);
            AssetDatabase.SaveAssets();
            Dictionary<string, object> textureDry = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, false);
            var dryFindings = ((List<object>)((Dictionary<string, object>)textureDry["preflight_now"])["findings"])
                .Cast<Dictionary<string, object>>().Select(f => (string)f["code"]).ToList();
            Check("texture without streaming mipmaps: preflight error, dry run lists it and changes nothing",
                  dryFindings.Contains("sdk_texture_streaming_mipmaps") &&
                  ((List<object>)textureDry["textures_that_would_get_streaming"]).Cast<string>().SequenceEqual(new[] { TexturePath }) &&
                  !((TextureImporter)AssetImporter.GetAtPath(TexturePath)).streamingMipmaps,
                  string.Join(",", dryFindings));
            Dictionary<string, object> textureFix = AvatarMaterialSetup.Execute(Copy, AvatarMaterialSetup.DefaultShader, true);
            Check("texture without streaming mipmaps: apply turns it on and the preflight passes",
                  ((TextureImporter)AssetImporter.GetAtPath(TexturePath)).streamingMipmaps && (bool)textureFix["settings_changed"] &&
                  (string)textureFix["status"] == "pass",
                  textureFix["status"] + " / " + textureFix["mode"]);

            bool refused = false;
            try { AvatarMaterialSetup.Execute(Copy, "Standard", false); } catch (ArgumentException) { refused = true; }
            Check("a shader that is not on the SDK's Quest whitelist is refused", refused, "");

            AssetDatabase.DeleteAsset(TempFolder);
            Check("ready fixture unchanged and the temporary folder removed",
                  Hash(Ready) + Hash(Ready + ".meta") == before && !Directory.Exists(TempFolder) && !File.Exists(TempFolder + ".meta"), "");

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-material-setup-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon material setup self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        static string Plan(Dictionary<string, object> report, string key)
        {
            return string.Join("; ", ((List<object>)report[key]).Cast<Dictionary<string, object>>()
                .Select(m => m["material"] + ": " + m["embedded_shader"] + " -> " + m["action"]));
        }

        static string QuestStatus(Dictionary<string, object> report, string key)
        {
            var preflight = (Dictionary<string, object>)report[key];
            return (string)((Dictionary<string, object>)((Dictionary<string, object>)preflight["platforms"])["quest"])["status"];
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
