// Frosty Neon avatar material setup (Editor-only, batch-only).
//
// A model imported with Unity's defaults gets materials generated inside the import, using Unity's
// Standard shader. VRChat SDK 3.10.6 rejects that shader on Quest avatars. This tool gives the model
// one set of materials that is allowed on both PC and Quest:
//   1. For each material embedded in the model, it creates a material asset next to the model
//      (<model folder>/Materials/<material name>.mat) using a shader from the SDK's Quest whitelist,
//      copying the colour, main texture (with tiling and offset), metallic and smoothness values where the shader has them.
//   2. It points the model's import settings at those assets (a material remap in the .meta file).
//   3. It turns on Streaming Mip Maps for mipmapped textures those materials use (an SDK upload error otherwise).
// The model file itself is never modified. Then it runs the preflight.
//
// It changes nothing unless -materialApply is given; the default is a dry run that only reports.
// Existing material assets are never overwritten: an existing <name>.mat is reused as it is.
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarMaterialSetup.Run
//       -materialAsset Assets/Path/Avatar.fbx [-materialShader "VRChat/Mobile/Standard Lite"]
//       [-materialApply] [-materialOut report.json] -logFile <log>
//   Exit code 0 = finished and the preflight afterwards is pass or warn; 1 = preflight fail or it could not run.
// See docs/AVATAR_MATERIAL_SETUP.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarMaterialSetup
    {
        // Has a colour property, so it suits untextured models. Toon Lit and Diffuse only take a texture.
        public const string DefaultShader = "VRChat/Mobile/Standard Lite";
        static readonly string[] CopiedFloats = { "_Metallic", "_Glossiness" };

        public static void Run()
        {
            int code = 1;
            try
            {
                string asset = Argument("-materialAsset");
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -materialAsset <Assets/...> (an imported model file).");
                bool apply = Array.IndexOf(Environment.GetCommandLineArgs(), "-materialApply") >= 0;
                Dictionary<string, object> report = Execute(asset, Argument("-materialShader") ?? DefaultShader, apply);
                string outPath = Argument("-materialOut") ??
                                 "Logs/FrostyNeon/materials_" + Path.GetFileNameWithoutExtension(asset) + ".json";
                string written = AvatarPreflight.WriteReport(report, outPath);
                Debug.Log("Frosty Neon material setup: " + report["mode"] + ", status " + report["status"] + ", report written to " + written);
                code = (string)report["status"] == "fail" ? 1 : 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon material setup could not run: " + error);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static Dictionary<string, object> Execute(string assetPath, string shaderName, bool apply)
        {
            bool requested = apply;
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                throw new FileNotFoundException("Not an imported model file: " + assetPath);
            if (!VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList.Contains(shaderName))
                throw new ArgumentException("'" + shaderName + "' is not on the VRChat SDK's Quest shader whitelist: " +
                                            string.Join(", ", VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList));
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new ArgumentException("Shader not found in the project: " + shaderName);

            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/Materials";
            var plan = new List<object>();
            var work = new List<(Material embedded, string path, bool exists)>();
            foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>()
                                                       .OrderBy(m => m.name, StringComparer.Ordinal))
            {
                string path = folder + "/" + embedded.name + ".mat";
                bool exists = AssetDatabase.LoadAssetAtPath<Material>(path) != null;
                work.Add((embedded, path, exists));
                plan.Add(new Dictionary<string, object>
                {
                    ["material"] = embedded.name,
                    ["embedded_shader"] = embedded.shader ? embedded.shader.name : null,
                    ["material_asset"] = path,
                    ["action"] = exists ? "reuse existing asset unchanged" : "create with shader " + shaderName,
                    ["colour"] = embedded.HasProperty("_Color") ? "#" + ColorUtility.ToHtmlStringRGBA(embedded.color) : null,
                });
            }

            var report = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-material-setup",
                ["unity_version"] = Application.unityVersion,
                ["mode"] = apply ? "applied" : "dry_run",
                ["asset"] = assetPath,
                ["shader"] = shaderName,
                ["settings_changed"] = false,
                [apply ? "materials" : "materials_that_would_be_set_up"] = plan,
            };
            if (work.Count == 0)
            {
                report["note"] = "The model has no embedded materials; nothing to do.";
                report["mode"] = "nothing_to_do";
                apply = false;
            }

            if (apply)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), "Materials");
                foreach (var item in work)
                {
                    Material material = item.exists ? AssetDatabase.LoadAssetAtPath<Material>(item.path) : Create(item.embedded, shader, item.path);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), item.embedded.name), material);
                }
                AssetDatabase.SaveAssets();
                importer.SaveAndReimport();
                report["settings_changed"] = true;
            }

            // Textures the materials use: VRChat's SDK refuses mipmapped textures without streaming mipmaps.
            List<string> textures = FixTextureStreaming(assetPath, requested);
            report[requested ? "textures_streaming_enabled" : "textures_that_would_get_streaming"] = textures.Cast<object>().ToList();
            if (requested && textures.Count > 0)
            {
                report["settings_changed"] = true;
                if ((string)report["mode"] == "nothing_to_do") report["mode"] = "applied";
            }

            Dictionary<string, object> preflight = AvatarPreflight.AnalyzeAsset(assetPath, null);
            report[apply || (requested && textures.Count > 0) ? "preflight_after" : "preflight_now"] = preflight;
            report["status"] = preflight["status"];
            return report;
        }

        /// <summary>Turns on Streaming Mip Maps for the model's mipmapped textures (when apply); returns their paths.</summary>
        public static List<string> FixTextureStreaming(string assetPath, bool apply)
        {
            UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            List<TextureImporter> importers;
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), scene);
                importers = AvatarPreflight.TexturesWithoutStreamingMipmaps(instance.GetComponentsInChildren<Renderer>(true));
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            if (apply)
                foreach (TextureImporter importer in importers)
                {
                    importer.streamingMipmaps = true;
                    importer.SaveAndReimport();
                }
            return importers.Select(i => i.assetPath).ToList();
        }

        static Material Create(Material embedded, Shader shader, string path)
        {
            var material = new Material(shader) { name = embedded.name };
            CopyProperties(embedded, material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Copies what both shaders have in common: colour, main texture with its tiling and offset,
        /// metallic and smoothness. Anything else on the source material is not carried over.
        /// </summary>
        public static void CopyProperties(Material from, Material to)
        {
            if (from.HasProperty("_Color") && to.HasProperty("_Color")) to.color = from.color;
            if (from.HasProperty("_MainTex") && to.HasProperty("_MainTex"))
            {
                to.mainTexture = from.mainTexture;
                to.mainTextureScale = from.mainTextureScale;
                to.mainTextureOffset = from.mainTextureOffset;
            }
            foreach (string property in CopiedFloats)
                if (from.HasProperty(property) && to.HasProperty(property))
                    to.SetFloat(property, from.GetFloat(property));
        }
    }
}
