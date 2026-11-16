// Frosty Neon avatar descriptor setup (Editor-only, batch-only).
//
// VRChat needs an avatar root that carries a VRC Avatar Descriptor (and the Pipeline Manager that
// comes with it). This tool creates that as a separate prefab next to the model, so the model file
// and its import settings stay untouched:
//   <model folder>/<model name>_Avatar.prefab = the imported model + VRCAvatarDescriptor + PipelineManager
// It sets one thing on the descriptor, the view position (where the wearer's eyes are):
//   * the midpoint of the eye bones when both are mapped, otherwise
//   * an estimate from the part of the mesh skinned to the head bone (62% up, at its front surface).
// When the face mesh has all 15 of VRChat's viseme blend shapes (vrc.v_sil ... vrc.v_ou, or v_sil ...),
// it also wires lip sync so the mouth moves with speech. Everything else on the descriptor is left at
// the SDK's defaults (playable layers, expressions, eye look). The Pipeline Manager's blueprint ID is left empty: nothing is uploaded.
//
// It creates nothing unless -descriptorApply is given, and never overwrites an existing prefab.
// Written against VRChat SDK 3.10.6.
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarDescriptorSetup.Run
//       -descriptorAsset Assets/Path/Avatar.fbx [-descriptorPrefab Assets/Path/Name_Avatar.prefab]
//       [-descriptorApply] [-descriptorOut report.json] -logFile <log>
//   Exit code 0 = finished and the preflight is pass or warn; 1 = preflight fail or it could not run.
// See docs/AVATAR_DESCRIPTOR_SETUP.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace FrostyNeon.Editor
{
    public static class AvatarDescriptorSetup
    {
        public static void Run()
        {
            int code = 1;
            try
            {
                string asset = Argument("-descriptorAsset");
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -descriptorAsset <Assets/...> (an imported model file).");
                bool apply = Array.IndexOf(Environment.GetCommandLineArgs(), "-descriptorApply") >= 0;
                Dictionary<string, object> report = Execute(asset, Argument("-descriptorPrefab"), apply);
                string outPath = Argument("-descriptorOut") ??
                                 "Logs/FrostyNeon/descriptor_" + Path.GetFileNameWithoutExtension(asset) + ".json";
                string written = AvatarPreflight.WriteReport(report, outPath);
                Debug.Log("Frosty Neon descriptor setup: " + report["mode"] + ", status " + report["status"] + ", report written to " + written);
                code = (string)report["status"] == "fail" ? 1 : 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon descriptor setup could not run: " + error);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static string DefaultPrefabPath(string assetPath)
        {
            return Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(assetPath) + "_Avatar.prefab";
        }

        public static Dictionary<string, object> Execute(string assetPath, string prefabPath, bool apply)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            // An imported model, or a prefab whose root is a humanoid (what UniVRM makes from a VRoid .vrm file).
            Animator rootAnimator = model ? model.GetComponent<Animator>() : null;
            bool humanoidPrefab = assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && rootAnimator != null && rootAnimator.isHuman;
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter) && !humanoidPrefab)
                throw new FileNotFoundException("Not an imported model file or a humanoid prefab: " + assetPath);
            prefabPath = string.IsNullOrEmpty(prefabPath) ? DefaultPrefabPath(assetPath) : prefabPath;
            if (!prefabPath.StartsWith("Assets/") || !prefabPath.EndsWith(".prefab"))
                throw new ArgumentException("The prefab path must be an Assets/... .prefab path: " + prefabPath);
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null || File.Exists(prefabPath);

            var report = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-descriptor-setup",
                ["unity_version"] = Application.unityVersion,
                ["mode"] = apply ? (exists ? "nothing_to_do" : "applied") : "dry_run",
                ["asset"] = assetPath,
                ["prefab"] = prefabPath,
                ["prefab_existed"] = exists,
                ["prefab_created"] = false,
            };
            if (exists)
                report["note"] = "The prefab already exists and was left as it is.";

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Vector3 view = FindViewPosition(instance, out string source);
                report["view_position"] = new List<object> { Round(view.x), Round(view.y), Round(view.z) };
                report["view_position_source"] = source;
                LipSyncMatch lipSync = FindVisemes(instance);
                report["lip_sync"] = lipSync.Describe();

                if (apply && !exists)
                {
                    instance.name = Path.GetFileNameWithoutExtension(prefabPath);
                    var descriptor = instance.AddComponent<VRCAvatarDescriptor>();
                    descriptor.ViewPosition = view;
                    ApplyLipSync(descriptor, lipSync);
                    if (instance.GetComponent<PipelineManager>() == null) instance.AddComponent<PipelineManager>();
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool saved);
                    if (!saved) throw new IOException("Unity could not save the prefab: " + prefabPath);
                    report["prefab_created"] = true;
                    exists = true;
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            // Preflight the avatar prefab when there is one, otherwise the model as it stands.
            Dictionary<string, object> preflight = AvatarPreflight.AnalyzeAsset(exists ? prefabPath : assetPath, null);
            report[exists ? "preflight_of_prefab" : "preflight_of_model"] = preflight;
            report["status"] = preflight["status"];
            return report;
        }

        /// <summary>
        /// Where the wearer's eyes are, relative to the avatar root: between the eye bones when both are
        /// mapped; otherwise estimated from the vertices skinned mainly to the head bone.
        /// </summary>
        public static Vector3 FindViewPosition(GameObject root, out string source)
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                throw new InvalidOperationException("The model is not imported as a humanoid; run the setup helper first.");
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye), right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left && right)
            {
                source = "midpoint of the eye bones";
                return root.transform.InverseTransformPoint((left.position + right.position) * 0.5f);
            }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (!head) throw new InvalidOperationException("The Head bone is not mapped.");
            bool any = false;
            var bounds = new Bounds();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                int boneIndex = Array.IndexOf(renderer.bones, head);
                if (!mesh || boneIndex < 0) continue;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float onHead = (w.boneIndex0 == boneIndex ? w.weight0 : 0f) + (w.boneIndex1 == boneIndex ? w.weight1 : 0f) +
                                   (w.boneIndex2 == boneIndex ? w.weight2 : 0f) + (w.boneIndex3 == boneIndex ? w.weight3 : 0f);
                    if (onHead < 0.5f) continue;
                    Vector3 point = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!any)
                throw new InvalidOperationException("No vertices are skinned to the Head bone, so the view position cannot be estimated.");
            source = "estimated from the head mesh (62% of its height, at its front surface); check it and adjust if needed";
            return new Vector3(bounds.center.x, bounds.min.y + 0.62f * bounds.size.y, bounds.max.z);
        }

        // ---- mouth movement (visemes)

        /// <summary>Result of looking for VRChat's 15 viseme blend shapes on one skinned mesh.</summary>
        public class LipSyncMatch
        {
            public SkinnedMeshRenderer Mesh;
            public string[] Names = new string[0];      // in the SDK's Viseme order; null where not found
            public List<string> Missing = new List<string>();
            public bool Complete => Mesh != null && Missing.Count == 0;

            public Dictionary<string, object> Describe()
            {
                return new Dictionary<string, object>
                {
                    ["visemes_found"] = Names.Count(n => n != null),
                    ["visemes_needed"] = (int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count,
                    ["face_mesh"] = Mesh ? Mesh.name : null,
                    ["missing"] = Missing.Cast<object>().ToList(),
                    ["will_wire_lip_sync"] = Complete,
                    ["note"] = Complete ? "All visemes found; lip sync will be set to Viseme Blend Shape." :
                               Mesh == null ? "No viseme blend shapes found; lip sync is left at the SDK default and the mouth will not move." :
                               "Some visemes are missing, so lip sync is not wired. Add the missing shapes or set it up by hand.",
                };
            }
        }

        /// <summary>
        /// Looks for the visemes the way the SDK's own descriptor editor does, using only its two strict rules:
        /// a blend shape named "vrc.v_&lt;viseme&gt;", else "v_&lt;viseme&gt;" (case-insensitive). The skinned mesh with
        /// the most matches is chosen. Read-only.
        /// </summary>
        public static LipSyncMatch FindVisemes(GameObject root)
        {
            int count = (int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count;
            var best = new LipSyncMatch();
            int bestFound = 0;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (!mesh) continue;
                var shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
                var match = new LipSyncMatch { Mesh = renderer, Names = new string[count] };
                for (int v = 0; v < count; v++)
                {
                    string viseme = ((VRC.SDKBase.VRC_AvatarDescriptor.Viseme)v).ToString().ToLowerInvariant();
                    match.Names[v] = shapes.FirstOrDefault(s => s.ToLowerInvariant() == "vrc.v_" + viseme) ??
                                     shapes.FirstOrDefault(s => s.ToLowerInvariant() == "v_" + viseme);
                    // Common spelling for the "E" viseme (seen on a 100Avatars model): vrc.v_ee.
                    if (match.Names[v] == null && viseme == "e")
                        match.Names[v] = shapes.FirstOrDefault(s => s.ToLowerInvariant() == "vrc.v_ee" || s.ToLowerInvariant() == "v_ee");
                    if (match.Names[v] == null) match.Missing.Add("vrc.v_" + viseme);
                }
                int found = count - match.Missing.Count;
                if (found > bestFound) { best = match; bestFound = found; }
            }
            if (best.Mesh == null)
                best.Missing = Enumerable.Range(0, count)
                    .Select(v => "vrc.v_" + ((VRC.SDKBase.VRC_AvatarDescriptor.Viseme)v).ToString().ToLowerInvariant()).ToList();
            return best;
        }

        /// <summary>Wires viseme lip sync on the descriptor when every viseme was found; otherwise leaves it alone.</summary>
        public static bool ApplyLipSync(VRCAvatarDescriptor descriptor, LipSyncMatch match)
        {
            if (!match.Complete) return false;
            descriptor.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            descriptor.VisemeSkinnedMesh = match.Mesh;
            descriptor.VisemeBlendShapes = (string[])match.Names.Clone();
            return true;
        }

        static double Round(float value)
        {
            return Math.Round(value, 4);
        }
    }
}
