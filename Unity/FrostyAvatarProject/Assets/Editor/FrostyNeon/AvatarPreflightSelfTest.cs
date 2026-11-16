// Self-test for AvatarPreflight (Editor-only). Runs the preflight on the generated fixtures and on
// objects built in memory, and checks the report against known expectations.
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarPreflightSelfTest.Run -logFile <log>
//   Exit code 0 = all checks passed, 1 = a check failed. Results: Logs/FrostyNeon/avatar_preflight_selftest.json,
//   plus one preflight report per fixture in the same folder.
//
// The only thing it adds to the project is the "ready" fixture (see PrepareReadyFixture), created once
// if it is missing. Existing fixtures and their import settings are hashed before and after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostyNeon.Editor
{
    public static class AvatarPreflightSelfTest
    {
        const string Fixtures = "Assets/FrostyNeon/Fixtures/";
        const string Basic = Fixtures + "fbx_fixture_v1.fbx";
        const string Humanoid = Fixtures + "fbx_humanoid_fixture_v1.fbx";
        const string ExplicitChest = Fixtures + "fbx_humanoid_fixture_v1_explicit_chest.fbx";
        const string Ready = Fixtures + "fbx_humanoid_fixture_v1_ready.fbx";
        const string OutPath = "Logs/FrostyNeon/avatar_preflight_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        [MenuItem("Frosty Neon/Developer/Run Avatar Preflight Self-Test", false, 2000)]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void Run()
        {
            bool ok = false;
            try { ok = Execute(); }
            catch (Exception error) { Debug.LogError("Frosty Neon preflight self-test crashed: " + error); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// A copy of the explicit-Chest humanoid fixture with mesh Read/Write and Legacy Blendshape Normals enabled, so that one fixture
        /// satisfies every check. The copy carries the explicit bone mapping in its import settings.
        /// </summary>
        public static void PrepareReadyFixture()
        {
            if (AssetImporter.GetAtPath(Ready) == null && !AssetDatabase.CopyAsset(ExplicitChest, Ready))
                throw new IOException("Could not copy " + ExplicitChest + " to " + Ready);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Ready);
            if (importer.isReadable && !AvatarPreflight.NeedsLegacyBlendShapeNormals(importer)) return;
            importer.isReadable = true;
            if (AvatarPreflight.NeedsLegacyBlendShapeNormals(importer)) AvatarPreflight.EnableLegacyBlendShapeNormals(importer);
            importer.SaveAndReimport();
        }

        static bool Execute()
        {
            Results.Clear();
            _failed = false;
            PrepareReadyFixture();
            string[] hashed = { Basic, Humanoid, ExplicitChest, Ready };
            Dictionary<string, string> before = Hashes(hashed);

            // 1. Fixtures as imported.
            Dictionary<string, object> basic = Preflight(Basic);
            Check("basic figure: fail; Read/Write disabled, no Animator (Generic import), Standard shader not allowed on Quest",
                  Status(basic) == "fail" && Codes(basic).SetEquals(new[] { "mesh_read_write_disabled", "sdk_no_animator", "sdk_quest_unsupported_shader", "sdk_legacy_blend_shape_normals" }),
                  Describe(basic));
            Check("basic figure: SDK statistics 14 triangles, 1 skinned mesh, 0 basic meshes, 1 material slot, 4 bones",
                  Statistics(basic, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount", "boneCount") == "14,1,0,1,4" &&
                  Statistics(basic, "quest", "polyCount", "skinnedMeshCount", "meshCount", "materialCount", "boneCount") == "14,1,0,1,4",
                  Statistics(basic, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount", "boneCount"));

            Dictionary<string, object> humanoid = Preflight(Humanoid);
            Check("humanoid rig, automatic mapping: fail; Chest missing, Read/Write disabled, Quest shader",
                  Status(humanoid) == "fail" && Codes(humanoid).SetEquals(new[] { "mesh_read_write_disabled", "sdk_spine_missing_elements", "sdk_quest_unsupported_shader", "sdk_legacy_blend_shape_normals" }) &&
                  MappedSlots(humanoid) == 20, Describe(humanoid) + " mapped=" + MappedSlots(humanoid));

            Dictionary<string, object> chest = Preflight(ExplicitChest);
            Check("humanoid rig, explicit Chest: Read/Write disabled and Quest shader remain; 21 slots mapped",
                  Status(chest) == "fail" && Codes(chest).SetEquals(new[] { "mesh_read_write_disabled", "sdk_quest_unsupported_shader", "sdk_legacy_blend_shape_normals" }) && MappedSlots(chest) == 21,
                  Describe(chest) + " mapped=" + MappedSlots(chest));

            Dictionary<string, object> ready = Preflight(Ready);
            Check("ready fixture: PC pass; Quest fail only because of the Standard shader; Excellent rank on both",
                  Status(ready) == "fail" && Codes(ready).SetEquals(new[] { "sdk_quest_unsupported_shader" }) &&
                  PlatformStatus(ready, "pc") == "pass" && PlatformStatus(ready, "quest") == "fail" &&
                  FindingPlatforms(ready, "sdk_quest_unsupported_shader") == "quest" && Rank(ready, "pc") == "Excellent" &&
                  Rank(ready, "quest") == "Excellent" && MeetsTarget(ready, "pc") && MeetsTarget(ready, "quest"),
                  Describe(ready) + " " + Rank(ready, "pc") + "/" + Rank(ready, "quest"));
            Check("ready fixture: SDK statistics 255 triangles, 1 skinned mesh, 0 basic meshes, 1 material slot, 21 bones",
                  Statistics(ready, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount", "boneCount") == "255,1,0,1,21",
                  Statistics(ready, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount", "boneCount"));

            // 2. Toolkit findings, provoked on a temporary copy of the ready fixture (the asset is not changed).
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Ready), scene);
                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                skinned.SetBlendShapeWeight(0, 100f);
                skinned.transform.localScale = new Vector3(100f, 100f, 100f);
                Dictionary<string, object> altered = AvatarPreflight.Analyze(instance, null, Ready);
                Check("switched-on blend shape and scale 100 are reported as warnings (PC status warn)",
                      PlatformStatus(altered, "pc") == "warn" &&
                      Codes(altered).SetEquals(new[] { "blend_shape_default_weight_nonzero", "non_unit_scale", "sdk_quest_unsupported_shader" }),
                      Describe(altered));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            // 3. Ranks below Excellent: an object built in memory, 20,001 triangles, 3 basic meshes, 5 material slots.
            var created = new List<UnityEngine.Object>();
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject heavy = BuildHeavyObject(scene, created);
                Dictionary<string, object> report = AvatarPreflight.Analyze(heavy, null);
                Check("heavy object: SDK statistics 20001 triangles, 0 skinned meshes, 3 basic meshes, 5 material slots",
                      Statistics(report, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount") == "20001,0,3,5",
                      Statistics(report, "pc", "polyCount", "skinnedMeshCount", "meshCount", "materialCount"));
                Check("heavy object: PC Good (on target), Quest VeryPoor (below target), reported for Quest only",
                      Rank(report, "pc") == "Good" && MeetsTarget(report, "pc") && Rank(report, "quest") == "VeryPoor" &&
                      !MeetsTarget(report, "quest") && FindingPlatforms(report, "performance_below_target") == "quest",
                      Rank(report, "pc") + "/" + Rank(report, "quest") + " platforms=" + FindingPlatforms(report, "performance_below_target"));
                Check("heavy object: Quest categories over target are triangles, basic meshes and material slots",
                      OverTarget(report, "quest") == "MaterialCount,MeshCount,PolyCount", OverTarget(report, "quest"));

                report = AvatarPreflight.Analyze(heavy, new Dictionary<string, string> { ["pc"] = "Excellent", ["quest"] = "Very Poor" });
                Check("target ranks are configurable: PC Excellent target now fails, Quest VeryPoor target now passes",
                      FindingPlatforms(report, "performance_below_target") == "pc" && OverTarget(report, "pc") == "MaterialCount",
                      FindingPlatforms(report, "performance_below_target") + " over=" + OverTarget(report, "pc"));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (UnityEngine.Object item in created) UnityEngine.Object.DestroyImmediate(item);
            }

            // 3b. Shaders: the SDK's Quest whitelist, and the material details in the report.
            var readyMaterial = (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)
                ((List<object>)ready["renderers"])[0])["material_details"])[0];
            Check("material details: fixture material uses Standard and is embedded in the model",
                  (string)readyMaterial["shader"] == "Standard" && ((string)readyMaterial["source"]).StartsWith("embedded in "),
                  readyMaterial["shader"] + " / " + readyMaterial["source"]);
            created = new List<UnityEngine.Object>();
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Shader mobile = Shader.Find("VRChat/Mobile/Toon Lit");
                var root = new GameObject("ShaderObject");
                SceneManager.MoveGameObjectToScene(root, scene);
                created.Add(root);
                AddMesh(root, "MobilePart", 1, 1, mobile, created);
                Dictionary<string, object> report = AvatarPreflight.Analyze(root, null);
                Check("a VRChat/Mobile shader is accepted for Quest", mobile != null && !Codes(report).Contains("sdk_quest_unsupported_shader"),
                      mobile != null ? mobile.name : "shader not found");
                AddMesh(root, "StandardPart", 1, 1, Shader.Find("Standard"), created);
                report = AvatarPreflight.Analyze(root, null);
                var finding = Findings(report).FirstOrDefault(f => (string)f["code"] == "sdk_quest_unsupported_shader");
                Check("a Standard shader beside it is reported for Quest only, naming just that shader",
                      finding != null && (string)finding["platform"] == "quest" && (string)finding["severity"] == "error" &&
                      string.Join(",", ((List<object>)finding["objects"]).Cast<string>()) == "Standard",
                      finding != null ? string.Join(",", ((List<object>)finding["objects"]).Cast<string>()) : "no finding");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (UnityEngine.Object item in created) UnityEngine.Object.DestroyImmediate(item);
            }

            // 4. Input handling and non-interference.
            bool rejected = false;
            try { AvatarPreflight.ParseRating("Great"); } catch (ArgumentException) { rejected = true; }
            Check("unknown rank is refused", rejected, "");
            bool missing = false;
            try { AvatarPreflight.AnalyzeAsset("Assets/DoesNotExist.fbx", null); } catch (FileNotFoundException) { missing = true; }
            Check("missing asset is refused", missing, "");

            Dictionary<string, string> after = Hashes(hashed);
            Check("fixture files and import settings unchanged by the preflight",
                  before.Count == after.Count && before.All(p => after[p.Key] == p.Value),
                  string.Join(", ", before.Where(p => after[p.Key] != p.Value).Select(p => p.Key)));
            Check("no scene was left modified", !SceneManager.GetActiveScene().isDirty, SceneManager.GetActiveScene().name);

            var summary = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-preflight-selftest",
                ["unity_version"] = Application.unityVersion,
                ["vrchat_sdk_avatars_version"] = ready["vrchat_sdk_avatars_version"],
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            };
            AvatarPreflight.WriteReport(summary, OutPath);
            Debug.Log("Frosty Neon preflight self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        static Dictionary<string, object> Preflight(string asset)
        {
            Dictionary<string, object> report = AvatarPreflight.AnalyzeAsset(asset, null);
            AvatarPreflight.WriteReport(report, AvatarPreflight.DefaultReportPath(asset));
            return report;
        }

        // One mesh with 19,999 triangles in 3 submeshes plus two single-triangle props: 3 Mesh Renderers, 5 material slots.
        static GameObject BuildHeavyObject(Scene scene, List<UnityEngine.Object> created)
        {
            var root = new GameObject("HeavyObject");
            SceneManager.MoveGameObjectToScene(root, scene);
            created.Add(root);
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            AddMesh(root, "Body", 19999, 3, shader, created);
            AddMesh(root, "PropA", 1, 1, shader, created);
            AddMesh(root, "PropB", 1, 1, shader, created);
            return root;
        }

        static void AddMesh(GameObject root, string name, int triangles, int submeshes, Shader shader, List<UnityEngine.Object> created)
        {
            var vertices = new Vector3[triangles * 3];
            for (int i = 0; i < triangles; i++)
            {
                float x = (i % 200) * 0.005f, y = (i / 200) * 0.005f;
                vertices[3 * i] = new Vector3(x, y, 0);
                vertices[3 * i + 1] = new Vector3(x + 0.004f, y, 0);
                vertices[3 * i + 2] = new Vector3(x, y + 0.004f, 0);
            }
            var mesh = new Mesh { name = name, vertices = vertices, subMeshCount = submeshes };
            for (int s = 0; s < submeshes; s++)
            {
                int first = triangles * s / submeshes, last = triangles * (s + 1) / submeshes;
                mesh.SetTriangles(Enumerable.Range(first * 3, (last - first) * 3).ToArray(), s);
            }
            mesh.RecalculateBounds();
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var materials = new Material[submeshes];
            for (int s = 0; s < submeshes; s++)
            {
                materials[s] = new Material(shader) { name = name + "_Mat" + s };
                created.Add(materials[s]);
            }
            child.AddComponent<MeshRenderer>().sharedMaterials = materials;
            created.Add(mesh);
        }

        // ---- report readers

        static string Status(Dictionary<string, object> report) { return (string)report["status"]; }

        static IEnumerable<Dictionary<string, object>> Findings(Dictionary<string, object> report)
        {
            return ((List<object>)report["findings"]).Cast<Dictionary<string, object>>();
        }

        static HashSet<string> Codes(Dictionary<string, object> report)
        {
            return new HashSet<string>(Findings(report).Select(f => (string)f["code"]));
        }

        static string Describe(Dictionary<string, object> report)
        {
            return Status(report) + " [" + string.Join(", ", Codes(report).OrderBy(c => c)) + "]";
        }

        static Dictionary<string, object> Platform(Dictionary<string, object> report, string platform)
        {
            return (Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])[platform];
        }

        static string PlatformStatus(Dictionary<string, object> report, string platform)
        {
            return (string)Platform(report, platform)["status"];
        }

        static string Rank(Dictionary<string, object> report, string platform)
        {
            return (string)Platform(report, platform)["sdk_overall_rank"];
        }

        static bool MeetsTarget(Dictionary<string, object> report, string platform)
        {
            return (bool)Platform(report, platform)["meets_target"];
        }

        static string OverTarget(Dictionary<string, object> report, string platform)
        {
            return string.Join(",", ((List<object>)Platform(report, platform)["categories_over_target"]).Cast<string>().OrderBy(c => c));
        }

        static string Statistics(Dictionary<string, object> report, string platform, params string[] names)
        {
            var values = (Dictionary<string, object>)Platform(report, platform)["sdk_statistics"];
            return string.Join(",", names.Select(n => values.ContainsKey(n) ? Convert.ToString(values[n]) : "?"));
        }

        static string FindingPlatforms(Dictionary<string, object> report, string code)
        {
            return string.Join(",", Findings(report).Where(f => (string)f["code"] == code)
                                                     .Select(f => (string)f["platform"]).OrderBy(p => p));
        }

        static int MappedSlots(Dictionary<string, object> report)
        {
            var rig = (Dictionary<string, object>)report["rig"];
            return rig.ContainsKey("mapped_slot_count") ? (int)rig["mapped_slot_count"] : 0;
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
