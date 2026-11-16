// Frosty Neon avatar asset preflight (Editor-only).
//
// Reads an imported avatar model (an FBX or prefab already in the project), places a temporary
// copy in a preview scene, and writes a JSON report with:
//   * the VRChat SDK's own performance statistics and ranks, once against the PC limits and
//     once against the mobile (Quest) limits;
//   * findings: the conditions of the SDK build panel's rig, size and mesh checks, re-evaluated
//     here, plus a few toolkit checks (blend shape weights, scale, missing materials).
// It changes no asset and no import setting, saves no scene, and does not build or upload.
//
// Written against VRChat SDK 3.10.6. The SDK checks marked "vrchat_sdk_condition" repeat the
// conditions of private methods in VRCSdkControlPanelAvatarBuilder (AnalyzeIK, the humanoid
// size checks, CheckAvatarMeshesForMeshReadWriteSetting); they do not call the build panel.
// Re-read those methods when the SDK is updated. See docs/AVATAR_PREFLIGHT.md.
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarPreflight.Run
//       -preflightAsset Assets/Path/Avatar.fbx [-preflightOut report.json]
//       [-preflightTargetPc Good] [-preflightTargetQuest Medium] -logFile <log>
//   Exit code 0 = status pass or warn, 1 = status fail or the preflight could not run.
// Menu: select a model in the Project window, then Frosty Neon > Preflight Selected Avatar Model.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace FrostyNeon.Editor
{
    public static class AvatarPreflight
    {
        public const int SchemaVersion = 4;
        public const string WrittenForSdk = "3.10.6";
        const string DefaultOutDirectory = "Logs/FrostyNeon";
        const string SdkSource = "vrchat_sdk_condition";
        const string ToolkitSource = "toolkit";

        // Platform name -> whether the SDK's mobile limits apply, and the default target rank.
        // Defaults match tools/blender/data/performance_budgets_v1.json.
        static readonly (string name, bool mobile, PerformanceRating target)[] Platforms =
        {
            ("pc", false, PerformanceRating.Good),
            ("quest", true, PerformanceRating.Medium),
        };

        // ------------------------------------------------------------------ entry points

        [MenuItem("Frosty Neon/Preflight Selected Avatar Model")]
        public static void RunFromMenu()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning("Frosty Neon preflight: select a model or prefab in the Project window first.");
                return;
            }
            Dictionary<string, object> report = AnalyzeAsset(path, null);
            string written = WriteReport(report, DefaultReportPath(path));
            Debug.Log("Frosty Neon preflight: status " + report["status"] + ", report written to " + written);
        }

        public static void Run()
        {
            int code = 1;
            try
            {
                string asset = Argument("-preflightAsset");
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -preflightAsset <Assets/...> (a model or prefab in the project).");
                var targets = new Dictionary<string, string>();
                if (Argument("-preflightTargetPc") != null) targets["pc"] = Argument("-preflightTargetPc");
                if (Argument("-preflightTargetQuest") != null) targets["quest"] = Argument("-preflightTargetQuest");

                Dictionary<string, object> report = AnalyzeAsset(asset, targets);
                string written = WriteReport(report, Argument("-preflightOut") ?? DefaultReportPath(asset));
                Debug.Log("Frosty Neon preflight: status " + report["status"] + ", report written to " + written);
                code = (string)report["status"] == "fail" ? 1 : 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon preflight could not run: " + error);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static string DefaultReportPath(string assetPath)
        {
            return DefaultOutDirectory + "/preflight_" + Path.GetFileNameWithoutExtension(assetPath) + ".json";
        }

        public static string WriteReport(Dictionary<string, object> report, string outPath)
        {
            string fullPath = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, ToJson(report) + "\n", new UTF8Encoding(false));
            return fullPath;
        }

        // ------------------------------------------------------------------ analysis

        /// <summary>Preflight an imported model or prefab. The asset and its import settings are not changed.</summary>
        public static Dictionary<string, object> AnalyzeAsset(string assetPath, Dictionary<string, string> targetRanks)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new FileNotFoundException("No model or prefab at " + assetPath);

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                return Analyze(instance, targetRanks, assetPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Preflight an avatar root that already exists in a scene. Read-only.</summary>
        public static Dictionary<string, object> Analyze(GameObject root, Dictionary<string, string> targetRanks,
                                                         string assetPath = null)
        {
            var findings = new List<object>();
            var report = new Dictionary<string, object>
            {
                ["schema_version"] = SchemaVersion,
                ["tool"] = "frosty-neon-unity-avatar-preflight",
                ["unity_version"] = Application.unityVersion,
                ["vrchat_sdk_avatars_version"] = InstalledSdkVersion(),
                ["checks_written_for_sdk"] = WrittenForSdk,
                ["active_build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["asset"] = assetPath,
                ["avatar_root"] = root.name,
            };
            if ((string)report["vrchat_sdk_avatars_version"] != WrittenForSdk)
                Add(findings, "warning", "sdk_version_differs", ToolkitSource,
                    "Installed VRChat SDK is " + report["vrchat_sdk_avatars_version"] + "; the SDK-condition checks were " +
                    "written against " + WrittenForSdk + ". Re-read the SDK's checks before relying on them.");

            var importer = assetPath != null ? AssetImporter.GetAtPath(assetPath) as ModelImporter : null;
            report["import_settings"] = importer == null ? null : new Dictionary<string, object>
            {
                ["animation_type"] = importer.animationType.ToString(),
                ["global_scale"] = importer.globalScale,
                ["use_file_scale"] = importer.useFileScale,
                ["file_scale"] = importer.fileScale,
                ["bake_axis_conversion"] = importer.bakeAxisConversion,
                ["import_blend_shapes"] = importer.importBlendShapes,
                ["mesh_read_write_enabled"] = importer.isReadable,
                ["blend_shape_normals"] = importer.importBlendShapeNormals.ToString(),
                ["legacy_blend_shape_normals"] = LegacyBlendShapeNormals(importer),
            };

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            report["renderers"] = renderers.Select(DescribeRenderer).Cast<object>().ToList();
            CheckRenderers(root, renderers, findings);
            report["quest_shader_whitelist"] = VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList.Cast<object>().ToList();
            CheckQuestShaders(root, findings);

            var platforms = new Dictionary<string, object>();
            foreach (var platform in Platforms)
            {
                PerformanceRating target = platform.target;
                if (targetRanks != null && targetRanks.TryGetValue(platform.name, out string wanted))
                    target = ParseRating(wanted);
                platforms[platform.name] = PlatformSection(root, platform.name, platform.mobile, target, findings);
            }
            report["platforms"] = platforms;
            report["rig"] = RigSection(root, findings);
            report["avatar_descriptor"] = DescriptorSection(root);
            // SDK (OnGUIAvatarCheck): viseme lip sync without a face mesh is an error.
            var lipDescriptor = root.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>();
            if (lipDescriptor != null && lipDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                lipDescriptor.VisemeSkinnedMesh == null)
                Add(findings, "error", "sdk_viseme_face_mesh_missing", SdkSource,
                    "This avatar uses Visemes but the Face Mesh is not specified.");

            // Per-platform status: a finding without a platform counts for both.
            foreach (var platform in Platforms)
            {
                var relevant = findings.Cast<Dictionary<string, object>>()
                    .Where(f => !f.ContainsKey("platform") || (string)f["platform"] == platform.name).ToList();
                ((Dictionary<string, object>)platforms[platform.name])["status"] =
                    relevant.Any(f => (string)f["severity"] == "error") ? "fail" :
                    relevant.Any(f => (string)f["severity"] == "warning") ? "warn" : "pass";
            }

            report["findings"] = findings;
            bool anyError = findings.Cast<Dictionary<string, object>>().Any(f => (string)f["severity"] == "error");
            bool anyWarning = findings.Cast<Dictionary<string, object>>().Any(f => (string)f["severity"] == "warning");
            report["status"] = anyError ? "fail" : anyWarning ? "warn" : "pass";
            report["limitations"] = new List<object>
            {
                "Not a VRChat SDK build: an avatar descriptor is reported when present but not required or judged, " +
                "nothing is built or uploaded, " +
                "and the SDK build panel is not opened. Of its validations, only the rig, size, mesh Read/Write, " +
                "legacy blend shape normals, Quest shader whitelist and texture streaming / size conditions are covered here; components, descriptor settings, upload size " +
                "and the rest are not.",
                "SDK-condition findings re-evaluate the conditions of private SDK " + WrittenForSdk + " build-panel methods; " +
                "they are not produced by the panel itself.",
                "Quest ranks use the SDK's mobile limits on the assets as imported for the active build target " +
                "shown above. Android texture compression and download size are not evaluated. The Quest shader " +
                "check calls the SDK's whitelist check directly; the SDK itself only runs it when the build target " +
                "is Android or iOS.",
                "Statistics for PhysBones, contacts, constraints and other components are whatever is on the " +
                "analysed object; a bare model has none, so they do not predict the finished avatar.",
            };
            return report;
        }

        static string InstalledSdkVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.vrchat.avatars/package.json");
            return info != null ? info.version : null;
        }

        public static PerformanceRating ParseRating(string text)
        {
            string compact = (text ?? "").Replace(" ", "");
            if (Enum.TryParse(compact, true, out PerformanceRating rating) && rating != PerformanceRating.None)
                return rating;
            throw new ArgumentException("Unknown rank '" + text + "'. Use Excellent, Good, Medium, Poor or VeryPoor.");
        }

        // ---- performance: the SDK's own calculation, per platform

        static Dictionary<string, object> PlatformSection(GameObject root, string name, bool mobile,
                                                          PerformanceRating target, List<object> findings)
        {
            var stats = new AvatarPerformanceStats(mobile);
            AvatarPerformance.CalculatePerformanceStats(root.name, root, stats, mobile);

            var ratings = new Dictionary<string, object>();
            var over = new List<string>();
            foreach (AvatarPerformanceCategory category in Enum.GetValues(typeof(AvatarPerformanceCategory)))
            {
                if (category == AvatarPerformanceCategory.None ||
                    category == AvatarPerformanceCategory.AvatarPerformanceCategoryCount) continue;
                PerformanceRating rating = stats.GetPerformanceRatingForCategory(category);
                if (rating == PerformanceRating.None) continue;          // not rated by the SDK for this object
                ratings[category.ToString()] = rating.ToString();
                if (category != AvatarPerformanceCategory.Overall && rating > target) over.Add(category.ToString());
            }
            PerformanceRating overall = stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall);

            var values = new Dictionary<string, object>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(AvatarPerformanceStats).GetFields(flags))
                AddStatistic(values, field.Name, field.FieldType, () => field.GetValue(stats));
            foreach (PropertyInfo property in typeof(AvatarPerformanceStats).GetProperties(flags))
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    AddStatistic(values, property.Name, property.PropertyType, () => property.GetValue(stats));

            if (overall > target)
                Add(findings, "warning", "performance_below_target", "vrchat_sdk_performance",
                    PlatformLabel(name) + ": VRChat SDK rank " + overall + ", target " + target + ". Categories over the target: " +
                    string.Join(", ", over.Select(c => c + " (" + ratings[c] + ")")) + ".", platform: name);

            return new Dictionary<string, object>
            {
                ["limits"] = mobile ? "VRChat SDK mobile limits (Android, iOS, Quest)" : "VRChat SDK PC limits",
                ["sdk_overall_rank"] = overall.ToString(),
                ["target_rank"] = target.ToString(),
                ["meets_target"] = overall <= target,
                ["categories_over_target"] = over.Cast<object>().ToList(),
                ["sdk_category_ranks"] = ratings,
                ["sdk_statistics"] = values,
            };
        }

        static string PlatformLabel(string name)
        {
            return name == "pc" ? "PC" : "Quest";
        }

        static void AddStatistic(Dictionary<string, object> values, string name, Type type, Func<object> read)
        {
            Type inner = Nullable.GetUnderlyingType(type) ?? type;
            if (name == "avatarName" || values.ContainsKey(name)) return;
            if (!(inner.IsPrimitive || inner.IsEnum || inner == typeof(Bounds))) return;
            object value;
            try { value = read(); }
            catch (Exception) { return; }
            if (value is Bounds bounds)
                value = new List<object> { Round(bounds.size.x), Round(bounds.size.y), Round(bounds.size.z) };
            else if (value != null && inner.IsEnum)
                value = value.ToString();
            values[name] = value;
        }

        // ---- renderers, meshes and materials

        static Dictionary<string, object> DescribeRenderer(Renderer renderer)
        {
            Mesh mesh = SharedMesh(renderer);
            var skinned = renderer as SkinnedMeshRenderer;
            return new Dictionary<string, object>
            {
                ["name"] = renderer.name,
                ["type"] = renderer.GetType().Name,
                ["mesh"] = mesh ? mesh.name : null,
                ["triangles"] = mesh ? TriangleCount(mesh) : 0,
                ["material_slots"] = renderer.sharedMaterials.Length,
                ["materials"] = renderer.sharedMaterials.Select(m => (object)(m ? m.name : null)).ToList(),
                ["material_details"] = renderer.sharedMaterials.Select(DescribeMaterial).ToList(),
                ["bones"] = skinned ? skinned.bones.Length : 0,
                ["blend_shapes"] = mesh ? mesh.blendShapeCount : 0,
            };
        }

        static object DescribeMaterial(Material material)
        {
            if (!material) return null;
            string path = AssetDatabase.GetAssetPath(material);
            string source = string.IsNullOrEmpty(path) ? "created in memory"
                : AssetDatabase.IsSubAsset(material) ? "embedded in " + path : "material asset " + path;
            return new Dictionary<string, object>
            {
                ["name"] = material.name,
                ["shader"] = material.shader ? material.shader.name : null,
                ["source"] = source,
                ["main_texture"] = material.HasProperty("_MainTex") && material.mainTexture ? material.mainTexture.name : null,
            };
        }

        // SDK: the build panel calls VRC.SDK3.Validation.AvatarValidation.FindIllegalShaders, but only when the
        // build target is Android or iOS. Called here on every run so Quest problems show up on a PC project too.
        static void CheckQuestShaders(GameObject root, List<object> findings)
        {
            List<string> illegal = VRC.SDK3.Validation.AvatarValidation.FindIllegalShaders(root)
                .Where(s => s != null).Select(s => s.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (illegal.Count == 0) return;
            Add(findings, "error", "sdk_quest_unsupported_shader", SdkSource,
                "Avatar uses unsupported shader" + (illegal.Count > 1 ? "s" : "") + " '" + string.Join("', '", illegal) +
                "'. You can only use the shaders provided in 'VRChat/Mobile' for Quest avatars. This does not affect PC.",
                platform: "quest", objects: illegal);
        }

        // Unity keeps this import setting internal; the SDK reads and writes it the same way.
        static readonly PropertyInfo LegacyNormalsProperty = typeof(ModelImporter).GetProperty(
            "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>The model's "Legacy Blendshape Normals" import setting; null if Unity no longer has it.</summary>
        public static bool? LegacyBlendShapeNormals(ModelImporter importer)
        {
            return LegacyNormalsProperty == null ? (bool?)null : (bool)LegacyNormalsProperty.GetValue(importer);
        }

        /// <summary>True when the SDK would reject the model: blend shape normals are calculated without the legacy option.</summary>
        public static bool NeedsLegacyBlendShapeNormals(ModelImporter importer)
        {
            return importer.importBlendShapeNormals == ModelImporterNormals.Calculate && LegacyBlendShapeNormals(importer) == false;
        }

        /// <summary>Turns "Legacy Blendshape Normals" on. The caller saves and reimports.</summary>
        public static void EnableLegacyBlendShapeNormals(ModelImporter importer) => SetLegacyBlendShapeNormals(importer, true);

        /// <summary>Sets "Legacy Blendshape Normals" (the Guided Fix's Undo turns it back off). The caller saves and reimports.</summary>
        public static void SetLegacyBlendShapeNormals(ModelImporter importer, bool on)
        {
            if (LegacyNormalsProperty == null)
                throw new InvalidOperationException("This Unity version has no 'Legacy Blendshape Normals' import setting.");
            LegacyNormalsProperty.SetValue(importer, on);
        }

        /// <summary>Texture importers used by these renderers' materials that have mipmaps but no streaming (an SDK error).</summary>
        public static List<TextureImporter> TexturesWithoutStreamingMipmaps(IEnumerable<Renderer> renderers)
        {
            var result = new List<TextureImporter>();
            foreach (Material material in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                foreach (int id in material.GetTexturePropertyNameIDs())
                {
                    Texture texture = material.GetTexture(id);
                    string path = texture ? AssetDatabase.GetAssetPath(texture) : null;
                    var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && importer.mipmapEnabled && !importer.streamingMipmaps && !result.Contains(importer))
                        result.Add(importer);
                }
            return result;
        }

        static Mesh SharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter ? filter.sharedMesh : null;
        }

        static long TriangleCount(Mesh mesh)
        {
            long total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) total += mesh.GetIndexCount(i) / 3;
            return total;
        }

        static void CheckRenderers(GameObject root, Renderer[] renderers, List<object> findings)
        {
            if (renderers.Length == 0)
                Add(findings, "error", "no_renderers", ToolkitSource, "The model has no mesh renderers.");

            // SDK: CheckAvatarMeshesForMeshReadWriteSetting. Only meshes that come from a model asset count.
            var unreadable = new List<string>();
            foreach (Mesh mesh in renderers.Select(SharedMesh).Where(m => m != null).Distinct())
            {
                if (!AssetDatabase.Contains(mesh)) continue;
                var meshImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
                if (meshImporter != null && !meshImporter.isReadable) unreadable.Add(mesh.name);
            }
            if (unreadable.Count > 0)
                Add(findings, "error", "mesh_read_write_disabled", SdkSource,
                    "This avatar contains meshes that were imported with Read/Write disabled. This must be fixed in the " +
                    "mesh import settings before uploading.", objects: unreadable);

            // SDK: CheckAvatarMeshesForLegacyBlendShapesSetting. It looks at the import setting, whether or not
            // the mesh has blend shapes. Found on 2026-11-14 when the SDK build stopped FrostyNeonBot on it.
            var wrongNormals = new List<string>();
            foreach (Mesh mesh in renderers.Select(SharedMesh).Where(m => m != null).Distinct())
            {
                if (!AssetDatabase.Contains(mesh)) continue;
                var meshImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
                if (meshImporter != null && NeedsLegacyBlendShapeNormals(meshImporter)) wrongNormals.Add(mesh.name);
            }
            if (wrongNormals.Count > 0)
                Add(findings, "error", "sdk_legacy_blend_shape_normals", SdkSource,
                    "This avatar contains skinned meshes that were imported with Blendshape Normals set to 'Calculate' but " +
                    "aren't using 'Legacy Blendshape Normals'. This will significantly increase the size of the uploaded " +
                    "avatar. This must be fixed in the mesh import settings before uploading.", objects: wrongNormals);

            // SDK (VerifyAvatarMipMapStreaming, VerifyMaxTextureSize): textures that block an upload. Found on
            // 2026-11-15 when the first textured avatar's upload stopped on the streaming check.
            List<TextureImporter> noStreaming = TexturesWithoutStreamingMipmaps(renderers);
            if (noStreaming.Count > 0)
                Add(findings, "error", "sdk_texture_streaming_mipmaps", SdkSource,
                    "This avatar has mipmapped textures without 'Streaming Mip Maps' enabled.",
                    objects: noStreaming.Select(t => t.assetPath).ToList());
            List<TextureImporter> oversize = VRCSdkControlPanel.GetOversizeTextureImporters(renderers.ToList());
            if (oversize.Count > 0)
                Add(findings, "error", "sdk_texture_too_large", SdkSource,
                    "This avatar has textures bigger than " + VRCSdkControlPanel.MAX_SDK_TEXTURE_SIZE +
                    ". Please reduce them to save memory for users.", objects: oversize.Select(t => t.assetPath).ToList());

            var noMesh = renderers.Where(r => !(r is ParticleSystemRenderer) && !(r is TrailRenderer) &&
                                              !(r is LineRenderer) && SharedMesh(r) == null).Select(r => r.name).ToList();
            if (noMesh.Count > 0)
                Add(findings, "warning", "renderer_without_mesh", ToolkitSource,
                    "Renderers with no mesh assigned.", objects: noMesh);

            var noMaterial = renderers.Where(r => r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => m == null))
                                      .Select(r => r.name).ToList();
            if (noMaterial.Count > 0)
                Add(findings, "warning", "missing_material", ToolkitSource,
                    "Renderers with an empty material slot.", objects: noMaterial);

            // Materials whose shader is not in the project (Unity draws them pink). Usual with Booth avatars, which
            // expect a shader package such as lilToon or Poiyomi to be added first. Added 2026-11-15 (stage 2).
            var missingShader = new List<string>();
            var neededPackages = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Material material in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                if (!ShaderMissing(material)) continue;
                string package = GuessShaderPackage(material);
                missingShader.Add(material.name + (package != null ? " (made for " + package + ")" : ""));
                if (package != null) neededPackages.Add(package);
            }
            if (missingShader.Count > 0)
                Add(findings, "error", "material_shader_missing", ToolkitSource,
                    missingShader.Count + " material(s) use a shader that is not in this project, so they show pink. " +
                    (neededPackages.Count > 0
                        ? "Add " + string.Join(" and ", neededPackages) + " to the project with VRChat Creator Companion (Manage Project), then check again."
                        : "Find out which shader the avatar's creator used (often named in its readme), add it, then check again."),
                    objects: missingShader);

            // Measured in this project: a Blender shape key left at a non-zero value imports switched on.
            var activeShapes = new List<string>();
            foreach (SkinnedMeshRenderer skinned in renderers.OfType<SkinnedMeshRenderer>())
            {
                if (!skinned.sharedMesh) continue;
                for (int i = 0; i < skinned.sharedMesh.blendShapeCount; i++)
                    if (Mathf.Abs(skinned.GetBlendShapeWeight(i)) > 1e-4f)
                        activeShapes.Add(skinned.name + ": " + skinned.sharedMesh.GetBlendShapeName(i) + " = " +
                                         Round(skinned.GetBlendShapeWeight(i)).ToString(CultureInfo.InvariantCulture));
            }
            if (activeShapes.Count > 0)
                Add(findings, "warning", "blend_shape_default_weight_nonzero", ToolkitSource,
                    "Blend shapes are switched on by default. Set the shape keys to 0 in the source file and export " +
                    "again unless this is intended.", objects: activeShapes);

            if (renderers.Length > 0)
            {
                Bounds all = renderers[0].bounds;
                foreach (Renderer renderer in renderers) all.Encapsulate(renderer.bounds);
                if (all.size.y < 0.3f)
                    Add(findings, "warning", "model_very_small", ToolkitSource,
                        "The model is only " + Round(all.size.y) + " m tall. If it should be human-sized, its units were " +
                        "probably read as centimetres; the setup helper scales it by 100 in that case.");
            }

            var scaled = root.GetComponentsInChildren<Transform>(true)
                .Where(t => (t.localScale - Vector3.one).sqrMagnitude > 1e-6f)
                .Select(t => t.name + " " + t.localScale.ToString("0.###")).ToList();
            if (scaled.Count > 0)
                Add(findings, "warning", "non_unit_scale", ToolkitSource,
                    scaled.Count + " object(s) or bone(s) have a scale other than 1. Check the export scale settings " +
                    "(first ten listed).", objects: scaled.Take(10).ToList());
        }

        // ---- rig: conditions of the SDK build panel's humanoid checks (SDK 3.10.6)

        static Dictionary<string, object> RigSection(GameObject root, List<object> findings)
        {
            var section = new Dictionary<string, object>();
            var animator = root.GetComponent<Animator>();
            section["has_animator"] = animator != null;
            section["is_humanoid"] = animator != null && animator.isHuman;
            if (animator == null)
            {
                Add(findings, "warning", "sdk_no_animator", SdkSource,
                    "This avatar does not contain an Animator, and will not animate in VRChat.");
                return section;
            }
            if (!animator.isHuman)
            {
                Add(findings, "warning", "sdk_not_humanoid", SdkSource,
                    "This avatar is not imported as a humanoid rig and will not play VRChat's provided animation set.");
                return section;
            }

            var mapped = new Dictionary<string, object>();
            foreach (HumanBodyBones slot in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (slot == HumanBodyBones.LastBone) continue;
                Transform transform = animator.GetBoneTransform(slot);
                if (transform) mapped[slot.ToString()] = transform.name;
            }
            section["humanoid_mapping"] = mapped;
            section["mapped_slot_count"] = mapped.Count;

            // Unity's stored humanoid skeleton must be the same size as the model: VRChat uses it as the rest
            // pose. Found 2026-11-15 on two avatars from centimetre files, whose stored skeleton was 100 times
            // the model; they loaded in VRChat with the eyes measured 1.4 m below the neck.
            float storedScale = StoredSkeletonScale(root);
            if (storedScale > 0f)
            {
                section["stored_skeleton_scale"] = Round(storedScale);
                if (Mathf.Abs(storedScale - 1f) > 0.05f)
                    Add(findings, "error", "humanoid_rest_pose_scale_mismatch", ToolkitSource,
                        "Unity's stored humanoid skeleton is " + Round(storedScale) + " times the size of the model. VRChat uses " +
                        "it as the avatar's rest pose, so the avatar breaks in VRChat. Prepare fixes this (it turns Convert " +
                        "Units off and moves the scale into Scale Factor).");
            }

            Func<HumanBodyBones, Transform> bone = animator.GetBoneTransform;
            Transform head = bone(HumanBodyBones.Head), lFoot = bone(HumanBodyBones.LeftFoot), rFoot = bone(HumanBodyBones.RightFoot);
            Transform lHand = bone(HumanBodyBones.LeftHand), rHand = bone(HumanBodyBones.RightHand);
            Transform lUpperArm = bone(HumanBodyBones.LeftUpperArm), rUpperArm = bone(HumanBodyBones.RightUpperArm);

            // Size and placement checks (ValidateFeatures / OnGUIAvatarCheck in the SDK).
            if (!lFoot || !rFoot)
                Add(findings, "error", "sdk_feet_not_specified", SdkSource, "Your avatar is humanoid, but its feet aren't specified!");
            else if ((lFoot.position - root.transform.position).y < 0)
                Add(findings, "warning", "sdk_feet_below_origin", SdkSource,
                    "Avatar feet are beneath the avatar's origin (the floor). That's probably not what you want.");
            if (!lUpperArm || !rUpperArm)
                Add(findings, "error", "sdk_upper_arms_not_specified", SdkSource,
                    "Your avatar is humanoid, but its upper arms aren't specified!");
            else
            {
                float height = (lUpperArm.position - root.transform.position).y;
                section["shoulder_height_m"] = Round(height);
                if (height < 0.2f)
                    Add(findings, "error", "sdk_too_short", SdkSource, "This avatar is too short. The minimum is 20cm shoulder height.");
                else if (height < 1.0f)
                    Add(findings, "warning", "sdk_shorter_than_average", SdkSource, "This avatar is shorter than average.");
                else if (height > 5.0f)
                    Add(findings, "warning", "sdk_too_tall", SdkSource, "This avatar is too tall. The maximum is 5m shoulder height.");
                else if (height > 2.5f)
                    Add(findings, "warning", "sdk_taller_than_average", SdkSource, "This avatar is taller than average.");
            }

            // AnalyzeIK: each failed error condition ends the SDK's check, so later ones are not evaluated.
            if (!head || !lFoot || !rFoot || !lHand || !rHand)
            {
                Add(findings, "error", "sdk_head_hands_feet_unmapped", SdkSource,
                    "Humanoid avatar must have head, hands and feet bones mapped.");
                return section;
            }

            Transform hips = bone(HumanBodyBones.Hips), spine = bone(HumanBodyBones.Spine), chest = bone(HumanBodyBones.Chest);
            Transform upperChest = bone(HumanBodyBones.UpperChest), neck = bone(HumanBodyBones.Neck);
            Transform lShoulder = bone(HumanBodyBones.LeftShoulder), rShoulder = bone(HumanBodyBones.RightShoulder);
            var missing = new List<string>();
            if (!neck) missing.Add("Neck");
            if (!lShoulder || !rShoulder) missing.Add("Shoulders");
            if (!hips) missing.Add("Pelvis");
            if (!spine) missing.Add("Spine");
            if (!chest) missing.Add("Chest");
            if (missing.Count > 0)
            {
                Add(findings, "error", "sdk_spine_missing_elements", SdkSource,
                    "Spine hierarchy missing elements, please map: " + string.Join(", ", missing) + ".", objects: missing);
                return section;
            }

            Transform expected = upperChest ? upperChest : chest;
            section["shoulders_and_neck_expected_parent"] = expected.name;
            if (lShoulder.parent != expected || rShoulder.parent != expected || neck.parent != expected)
            {
                Add(findings, "error", "sdk_spine_hierarchy_incorrect", SdkSource,
                    "Spine hierarchy incorrect. Make sure that the parent of both Shoulders and the Neck is the Chest " +
                    "(or UpperChest if set).", objects: new List<string> { lShoulder.name, rShoulder.name, neck.name, expected.name });
                return section;
            }

            Transform lLowerArm = bone(HumanBodyBones.LeftLowerArm), rLowerArm = bone(HumanBodyBones.RightLowerArm);
            if (!(FirstChildIs(lUpperArm, lLowerArm) && FirstChildIs(lLowerArm, lHand) &&
                  FirstChildIs(rUpperArm, rLowerArm) && FirstChildIs(rLowerArm, rHand)))
                Add(findings, "warning", "sdk_arm_first_child_order", SdkSource,
                    "LowerArm is not first child of UpperArm or Hand is not first child of LowerArm: you may have " +
                    "problems with Forearm rotations.");
            Transform lUpperLeg = bone(HumanBodyBones.LeftUpperLeg), lLowerLeg = bone(HumanBodyBones.LeftLowerLeg);
            Transform rUpperLeg = bone(HumanBodyBones.RightUpperLeg), rLowerLeg = bone(HumanBodyBones.RightLowerLeg);
            if (!(FirstChildIs(lUpperLeg, lLowerLeg) && FirstChildIs(lLowerLeg, lFoot) &&
                  FirstChildIs(rUpperLeg, rLowerLeg) && FirstChildIs(rLowerLeg, rFoot)))
                Add(findings, "warning", "sdk_leg_first_child_order", SdkSource,
                    "LowerLeg is not first child of UpperLeg or Foot is not first child of LowerLeg: you may have " +
                    "problems with Shin rotations.");
            if (!(lFoot.IsChildOf(hips) && rFoot.IsChildOf(hips) && lHand.IsChildOf(hips) && rHand.IsChildOf(hips)))
                Add(findings, "warning", "sdk_split_hierarchy", SdkSource,
                    "This avatar has a split hierarchy (Hips bone is not the ancestor of all humanoid bones). IK may " +
                    "not work correctly.");
            return section;
        }

        // Reported only; the preflight does not judge descriptor settings.
        static Dictionary<string, object> DescriptorSection(GameObject root)
        {
            var descriptor = root.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>();
            var pipeline = root.GetComponent<VRC.Core.PipelineManager>();
            var section = new Dictionary<string, object>
            {
                ["present"] = descriptor != null,
                ["pipeline_manager"] = pipeline != null,
                ["blueprint_id_set"] = pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId),
            };
            if (descriptor != null)
            {
                Vector3 view = descriptor.ViewPosition;
                section["view_position"] = new List<object> { Round(view.x), Round(view.y), Round(view.z) };
                section["lip_sync"] = descriptor.lipSync.ToString();
                var avatar3 = descriptor as VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
                if (avatar3 != null && avatar3.VisemeBlendShapes != null)
                    section["visemes_set"] = avatar3.VisemeBlendShapes.Count(n => !string.IsNullOrEmpty(n));
            }
            return section;
        }

        /// <summary>
        /// Size of the humanoid skeleton stored in the model's Avatar relative to the live model: the median of
        /// stored / live bone offsets. 1 when they agree; 0 when it cannot be measured.
        /// </summary>
        public static float StoredSkeletonScale(GameObject root)
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return 0f;
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root.transform && !byName.ContainsKey(t.name)) byName[t.name] = t;
            var ratios = new List<float>();
            foreach (SkeletonBone stored in animator.avatar.humanDescription.skeleton)
                if (byName.TryGetValue(stored.name, out Transform live) && live.localPosition.magnitude > 1e-3f)
                    ratios.Add(stored.position.magnitude / live.localPosition.magnitude);
            if (ratios.Count == 0) return 0f;
            ratios.Sort();
            return ratios[ratios.Count / 2];
        }

        static bool FirstChildIs(Transform parent, Transform child)
        {
            return parent && child && parent.childCount > 0 && parent.GetChild(0) == child;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>True when the material's shader is not in the project (Unity substitutes its pink error shader).</summary>
        public static bool ShaderMissing(Material material)
        {
            return material.shader == null || material.shader.name == "Hidden/InternalErrorShader";
        }

        /// <summary>
        /// The shader package a material was made for, judged from the property names saved in it (they survive when
        /// the shader itself is missing). Null when it cannot be told.
        /// </summary>
        public static string GuessShaderPackage(Material material)
        {
            var names = new List<string>();
            var saved = new SerializedObject(material).FindProperty("m_SavedProperties");
            foreach (string list in new[] { "m_TexEnvs", "m_Floats", "m_Ints", "m_Colors" })
            {
                SerializedProperty entries = saved?.FindPropertyRelative(list);
                if (entries == null) continue;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty first = entries.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                    if (first != null) names.Add(first.stringValue);
                }
            }
            if (names.Any(n => n.StartsWith("_lil", StringComparison.Ordinal))) return "lilToon";
            if (names.Any(n => n.StartsWith("_Poi", StringComparison.Ordinal) || n == "shader_master_label")) return "Poiyomi Toon Shader";
            if (names.Any(n => n == "_utsVersion" || n == "_utsTechnique")) return "Unity-Chan Toon Shader";
            return null;
        }

        static void Add(List<object> findings, string severity, string code, string source, string message,
                        string platform = null, List<string> objects = null)
        {
            var finding = new Dictionary<string, object>
            {
                ["severity"] = severity, ["code"] = code, ["source"] = source, ["message"] = message,
            };
            if (platform != null) finding["platform"] = platform;
            if (objects != null) finding["objects"] = objects.Cast<object>().ToList();
            findings.Add(finding);
        }

        static double Round(float value)
        {
            return Math.Round(value, 4);
        }

        /// <summary>Minimal JSON writer for the report's dictionaries, lists, strings, numbers and booleans.</summary>
        public static string ToJson(object value, int indent = 0)
        {
            string pad = new string(' ', indent * 2), inner = new string(' ', indent * 2 + 2);
            switch (value)
            {
                case null: return "null";
                case string text: return Quote(text);
                case bool flag: return flag ? "true" : "false";
                case IDictionary<string, object> map:
                    if (map.Count == 0) return "{}";
                    return "{\n" + string.Join(",\n", map.Select(p => inner + Quote(p.Key) + ": " + ToJson(p.Value, indent + 1))) +
                           "\n" + pad + "}";
                case IEnumerable list:
                    var items = list.Cast<object>().ToList();
                    if (items.Count == 0) return "[]";
                    if (items.All(i => i == null || i is string || i.GetType().IsPrimitive || i is double))
                        return "[" + string.Join(", ", items.Select(i => ToJson(i))) + "]";
                    return "[\n" + string.Join(",\n", items.Select(i => inner + ToJson(i, indent + 1))) + "\n" + pad + "]";
                case float single:
                    return float.IsNaN(single) || float.IsInfinity(single) ? "null" : Math.Round(single, 5).ToString("R", CultureInfo.InvariantCulture);
                case double number:
                    return double.IsNaN(number) || double.IsInfinity(number) ? "null" : number.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return Quote(value.ToString());
            }
        }

        static string Quote(string text)
        {
            var builder = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') builder.Append('\\').Append(c);
                else if (c == '\n') builder.Append("\\n");
                else if (c == '\r') builder.Append("\\r");
                else if (c == '\t') builder.Append("\\t");
                else if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                else builder.Append(c);
            }
            return builder.Append('"').ToString();
        }
    }
}
