// Editor-only measurement tool for the Blender FBX export profile.
// It reads how Unity imported an FBX that is already in the project, places a temporary
// copy in an empty scene, measures it and writes a JSON report. The plain probe saves
// nothing to the project. The humanoid probe makes one deliberate change: it sets the
// humanoid fixture's Animation Type to Humanoid, which Unity stores in the .fbx.meta file.
//
// Run from a shell (Editor must not have the project open):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.FbxImportProbe.Run
//       -probeFbx Assets/FrostyNeon/Fixtures/fbx_fixture_v1.fbx -probeOut <report.json> -logFile <log>
// Or from the menu: Frosty Neon > Developer > Probe FBX Fixture Import.
//
// See docs/FBX_EXPORT_PROFILE.md in the repository for what the numbers are compared with.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class FbxImportProbe
    {
        const string DefaultFbx = "Assets/FrostyNeon/Fixtures/fbx_fixture_v1.fbx";
        const string DefaultOut = "Logs/FrostyNeon/fbx_import_probe.json";

        const string HumanoidFbx = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1.fbx";
        const string HumanoidOut = "Logs/FrostyNeon/humanoid_probe.json";

        [MenuItem("Frosty Neon/Developer/Probe FBX Fixture Import", false, 2000)]
        public static void RunFromMenu()
        {
            string path = Measure(DefaultFbx, DefaultOut, false);
            Debug.Log("Frosty Neon FBX probe written to " + path);
        }

        // Unlike the plain probe, this one changes the fixture's import settings: it sets the
        // model's Animation Type to Humanoid (written to the .fbx.meta file) so that Unity
        // builds an Avatar with its automatic bone mapping, then reports that mapping.
        [MenuItem("Frosty Neon/Developer/Probe Humanoid Fixture Mapping", false, 2000)]
        public static void RunHumanoidFromMenu()
        {
            string path = Measure(HumanoidFbx, HumanoidOut, true);
            Debug.Log("Frosty Neon humanoid probe written to " + path);
        }

        // Entry points for -executeMethod. Exit code 0 = report written, 1 = failed.
        public static void Run()
        {
            RunAndExit(DefaultFbx, DefaultOut, false);
        }

        public static void RunHumanoid()
        {
            RunAndExit(HumanoidFbx, HumanoidOut, true);
        }

        // Experiment: does an explicit Chest assignment survive import? Works on a copy of the
        // humanoid fixture so the automatic-mapping result above stays reproducible.
        const string ExplicitFbx = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_explicit_chest.fbx";
        const string ExplicitOut = "Logs/FrostyNeon/humanoid_explicit_chest_probe.json";

        [MenuItem("Frosty Neon/Developer/Probe Humanoid Fixture With Explicit Chest", false, 2000)]
        public static void RunExplicitChestFromMenu()
        {
            AssignChest(ExplicitFbx, "Chest");
            Debug.Log("Frosty Neon explicit-chest probe written to " + Measure(ExplicitFbx, ExplicitOut, true));
        }

        public static void RunExplicitChest()
        {
            int code = 0;
            try
            {
                AssignChest(ExplicitFbx, Argument("-probeChestBone") ?? "Chest");
                Debug.Log("Frosty Neon probe written to " + Measure(ExplicitFbx, Argument("-probeOut") ?? ExplicitOut, true));
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon probe failed: " + error);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        // Imports the copy as Humanoid with Unity's automatic mapping, then writes that mapping back
        // into the import settings with the named bone added in the Chest slot, and re-imports.
        static void AssignChest(string fbxAssetPath, string chestBoneName)
        {
            if (AssetImporter.GetAtPath(fbxAssetPath) == null && !AssetDatabase.CopyAsset(HumanoidFbx, fbxAssetPath))
                throw new IOException("Could not copy " + HumanoidFbx + " to " + fbxAssetPath);
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxAssetPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = new HumanDescription();      // back to automatic mapping
            importer.SaveAndReimport();

            Avatar automatic = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath).OfType<Avatar>().FirstOrDefault();
            if (automatic == null || !automatic.isHuman)
                throw new InvalidOperationException("Unity did not build a humanoid Avatar for " + fbxAssetPath);
            HumanDescription description = automatic.humanDescription;
            var bones = description.human.Where(b => b.humanName != "Chest").ToList();
            bones.Add(new HumanBone { humanName = "Chest", boneName = chestBoneName,
                                      limit = new HumanLimit { useDefaultValues = true } });
            description.human = bones.ToArray();
            importer = (ModelImporter)AssetImporter.GetAtPath(fbxAssetPath);
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        static void RunAndExit(string defaultFbx, string defaultOut, bool humanoid)
        {
            int code = 0;
            try
            {
                string path = Measure(Argument("-probeFbx") ?? defaultFbx, Argument("-probeOut") ?? defaultOut, humanoid);
                Debug.Log("Frosty Neon probe written to " + path);
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon probe failed: " + error);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static string Measure(string fbxAssetPath, string outPath, bool humanoid)
        {
            var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
                throw new FileNotFoundException("No imported model at " + fbxAssetPath);
            bool changedImportSettings = false;
            if (humanoid && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
                changedImportSettings = true;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (prefab == null)
                throw new FileNotFoundException("No imported model at " + fbxAssetPath);

            var json = new StringBuilder();
            json.Append("{\n");
            Field(json, "tool", "frosty-neon-unity-fbx-import-probe");
            Field(json, "unity_version", Application.unityVersion);
            Field(json, "fbx_asset", fbxAssetPath);
            Field(json, "build_target", EditorUserBuildSettings.activeBuildTarget.ToString());

            json.Append("  \"import_settings\": {\n");
            Field(json, "global_scale", importer.globalScale, 4);
            Field(json, "use_file_scale", importer.useFileScale, 4);
            Field(json, "file_scale", importer.fileScale, 4);
            Field(json, "bake_axis_conversion", importer.bakeAxisConversion, 4);
            Field(json, "animation_type", importer.animationType.ToString(), 4);
            Field(json, "import_blend_shapes", importer.importBlendShapes, 4);
            Field(json, "is_readable", importer.isReadable, 4, last: true);
            json.Append("  },\n");

            // A new empty scene that is never saved; the instance is destroyed with it.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (humanoid)
            {
                // Unity's own result: the Avatar it built and which bone it put in each humanoid slot.
                var animator = instance.GetComponent<Animator>();
                Avatar avatar = animator ? animator.avatar : null;
                bool human = avatar != null && avatar.isValid && avatar.isHuman;
                json.Append("  \"avatar\": {\n");
                Field(json, "changed_import_settings_this_run", changedImportSettings, 4);
                Field(json, "present", avatar != null, 4);
                Field(json, "is_valid", avatar != null && avatar.isValid, 4);
                Field(json, "is_human", avatar != null && avatar.isHuman, 4, last: true);
                json.Append("  },\n");
                var mapped = new List<string>();
                foreach (HumanBodyBones slot in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (slot == HumanBodyBones.LastBone) continue;
                    Transform bone = human ? animator.GetBoneTransform(slot) : null;
                    mapped.Add("    " + Quote(slot.ToString()) + ": " + (bone ? Quote(bone.name) : "null"));
                }
                json.Append("  \"humanoid_mapping\": {\n").Append(string.Join(",\n", mapped)).Append("\n  },\n");

                // Second reading of the same Avatar: the mapping stored in its description.
                // This is the list the Rig > Configure window displays.
                var described = new List<string>();
                if (avatar != null)
                    foreach (HumanBone bone in avatar.humanDescription.human)
                        described.Add("    " + Quote(bone.humanName) + ": " + Quote(bone.boneName));
                json.Append("  \"avatar_description_mapping\": {\n").Append(string.Join(",\n", described)).Append("\n  },\n");

                // What the Configure window's Mapping > Automap button would produce. Unity keeps
                // that mapper internal, so it is called by reflection; a failure is reported, not hidden.
                json.Append("  \"configure_automap\": ").Append(ConfigureAutomap(instance.transform)).Append(",\n");
            }

            json.Append("  \"transforms\": [\n");
            var all = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                json.Append("    {\"name\": ").Append(Quote(t.name))
                    .Append(", \"parent\": ").Append(t.parent ? Quote(t.parent.name) : "null")
                    .Append(", \"local_euler\": ").Append(Vec(t.localEulerAngles))
                    .Append(", \"local_scale\": ").Append(Vec(t.localScale))
                    .Append(", \"world_position\": ").Append(Vec(t.position))
                    .Append(", \"world_up\": ").Append(Vec(t.up))
                    .Append("}").Append(i < all.Length - 1 ? ",\n" : "\n");
            }
            json.Append("  ],\n");

            json.Append("  \"renderers\": [\n");
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                var skinned = renderer as SkinnedMeshRenderer;
                Mesh shared = skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;

                // World-space vertex positions as rendered in the rest pose.
                Vector3[] world;
                if (skinned)
                {
                    var baked = new Mesh();
                    skinned.BakeMesh(baked, true);
                    world = baked.vertices.Select(v => skinned.transform.TransformPoint(v)).ToArray();
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                else
                {
                    world = shared.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray();
                }
                var unique = UniquePoints(world);
                Vector3 min = world.Aggregate(world[0], Vector3.Min), max = world.Aggregate(world[0], Vector3.Max);

                json.Append("    {\n");
                Field(json, "name", renderer.name, 6);
                Field(json, "type", renderer.GetType().Name, 6);
                Field(json, "vertex_count", shared.vertexCount, 6);
                Field(json, "triangle_count", shared.triangles.Length / 3, 6);
                Field(json, "submesh_count", shared.subMeshCount, 6);
                Raw(json, "materials", "[" + string.Join(", ", renderer.sharedMaterials.Select(m => Quote(m ? m.name : null))) + "]", 6);
                Raw(json, "bone_names", "[" + string.Join(", ", (skinned ? skinned.bones : new Transform[0]).Select(b => Quote(b ? b.name : null))) + "]", 6);
                Raw(json, "world_min", Vec(min), 6);
                Raw(json, "world_max", Vec(max), 6);
                Raw(json, "world_size", Vec(max - min), 6);
                Raw(json, "unique_world_vertices", "[" + string.Join(", ", unique.Select(Vec)) + "]", 6);

                json.Append("      \"blend_shapes\": [");
                for (int b = 0; b < shared.blendShapeCount; b++)
                {
                    var deltas = new Vector3[shared.vertexCount];
                    shared.GetBlendShapeFrameVertices(b, shared.GetBlendShapeFrameCount(b) - 1, deltas, null, null);
                    int moved = deltas.Count(d => d.sqrMagnitude > 1e-12f);
                    Vector3 largest = deltas.OrderByDescending(d => d.sqrMagnitude).First();
                    // The delta is in mesh space; express it in world space for comparison with Blender.
                    Vector3 largestWorld = renderer.transform.TransformVector(largest);
                    json.Append(b > 0 ? ", " : "").Append("{\"name\": ").Append(Quote(shared.GetBlendShapeName(b)))
                        .Append(", \"default_weight\": ").Append(skinned ? Num(skinned.GetBlendShapeWeight(b)) : "null")
                        .Append(", \"moved_vertices\": ").Append(moved)
                        .Append(", \"largest_delta_world\": ").Append(Vec(largestWorld)).Append("}");
                }
                json.Append("]\n");
                json.Append("    }").Append(r < renderers.Length - 1 ? ",\n" : "\n");
            }
            json.Append("  ],\n");

            // Unity's own list of humanoid bones and which of them are mandatory.
            json.Append("  \"human_trait\": {\n");
            Field(json, "bone_count", HumanTrait.BoneCount, 4);
            Field(json, "required_bone_count", HumanTrait.RequiredBoneCount, 4);
            var required = new List<string>();
            var optional = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
                (HumanTrait.RequiredBone(i) ? required : optional).Add(HumanTrait.BoneName[i]);
            Raw(json, "required", "[" + string.Join(", ", required.Select(Quote)) + "]", 4);
            Raw(json, "optional", "[" + string.Join(", ", optional.Select(Quote)) + "]", 4, last: true);
            json.Append("  }\n}\n");

            UnityEngine.Object.DestroyImmediate(instance);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            string fullPath = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, json.ToString(), new UTF8Encoding(false));
            return fullPath;
        }

        static string ConfigureAutomap(Transform root)
        {
            try
            {
                Type mapper = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarAutoMapper");
                if (mapper == null) return "{\"error\": \"UnityEditor.AvatarAutoMapper not found\"}";
                var method = mapper.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "MapBones" && m.GetParameters().Length == 2);
                if (method == null)
                {
                    string found = string.Join("; ", mapper.GetMethods(System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        .Where(m => m.Name == "MapBones").Select(m => m.ToString()));
                    return "{\"error\": " + Quote("No two-argument MapBones. Found: " + found) + "}";
                }
                var valid = new Dictionary<Transform, bool>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t != root) valid[t] = true;
                var result = method.Invoke(null, new object[] { root, valid }) as Dictionary<int, Transform>;
                if (result == null) return "{\"error\": \"MapBones returned an unexpected type\"}";
                var rows = result.OrderBy(pair => pair.Key).Select(pair =>
                    "    " + Quote(HumanTrait.BoneName[pair.Key]) + ": " + (pair.Value ? Quote(pair.Value.name) : "null"));
                return "{\n" + string.Join(",\n", rows) + "\n  }";
            }
            catch (Exception error)
            {
                return "{\"error\": " + Quote(error.GetType().Name + ": " + (error.InnerException ?? error).Message) + "}";
            }
        }

        static List<Vector3> UniquePoints(Vector3[] points)
        {
            var result = new List<Vector3>();
            foreach (Vector3 p in points)
                if (!result.Any(q => (q - p).sqrMagnitude < 1e-10f))
                    result.Add(p);
            return result;
        }

        static string Num(float value)
        {
            return Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
        }

        static string Vec(Vector3 v)
        {
            return "[" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + "]";
        }

        static string Quote(string text)
        {
            return text == null ? "null" : "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static void Raw(StringBuilder json, string key, string value, int indent = 2, bool last = false)
        {
            json.Append(' ', indent).Append(Quote(key)).Append(": ").Append(value).Append(last ? "\n" : ",\n");
        }

        static void Field(StringBuilder json, string key, string value, int indent = 2, bool last = false)
        {
            Raw(json, key, Quote(value), indent, last);
        }

        static void Field(StringBuilder json, string key, bool value, int indent = 2, bool last = false)
        {
            Raw(json, key, value ? "true" : "false", indent, last);
        }

        static void Field(StringBuilder json, string key, float value, int indent = 2, bool last = false)
        {
            Raw(json, key, Num(value), indent, last);
        }

        static void Field(StringBuilder json, string key, int value, int indent = 2, bool last = false)
        {
            Raw(json, key, value.ToString(CultureInfo.InvariantCulture), indent, last);
        }
    }
}
