// Frosty Neon avatar setup helper (Editor-only).
//
// Prepares an imported avatar model's IMPORT SETTINGS so that it clears the two problems every
// default import has with VRChat SDK 3.10.6 (see docs/UNITY_VALIDATION.md), then runs the preflight:
//   1. Animation Type -> Humanoid, Avatar created from the model.
//   2. Mesh Read/Write -> enabled; "Legacy Blendshape Normals" -> enabled when blend shape normals
//      are set to Calculate (the SDK build rejects the model otherwise).
//   2a. Convert Units: a file in other units (centimetres) has the conversion moved into Scale Factor, so
//      Unity's stored humanoid skeleton matches the model (VRChat uses it as the rest pose).
//   2b. Size: with -setupHeight <metres>, Scale Factor is adjusted so the model is that tall. Without it,
//      a model under 0.3 m tall that is 0.5 to 3 m when multiplied by 100 is scaled by 100 (a model
//      exported with its units read as centimetres).
//   3. If Unity's automatic bone mapping left the Chest slot empty, and exactly one suitable bone
//      exists (the common parent of the neck and both shoulders, not already used for another
//      slot), that bone is written into the Chest slot. Otherwise nothing is guessed.
// It never touches the model file itself, only the settings Unity keeps in the .meta file.
//
// It does nothing to your asset unless told to:
//   default            dry run: the changes are tried on a temporary copy, reported, and the copy deleted.
//   -setupApply -setupCopyTo Assets/.../Name.fbx   apply to a new copy; the original is untouched.
//   -setupApply        apply in place to the asset's own import settings.
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarSetupHelper.Run
//       -setupAsset Assets/Path/Avatar.fbx [-setupApply] [-setupCopyTo Assets/Path/Copy.fbx]
//       [-setupOut report.json] -logFile <log>
//   Exit code 0 = finished and the preflight afterwards is pass or warn; 1 = preflight fail,
//   a problem was reported, or the helper could not run.
// See docs/AVATAR_SETUP_HELPER.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostyNeon.Editor
{
    public static class AvatarSetupHelper
    {
        public const string DryRunFolder = "Assets/FrostyNeon/_SetupDryRun";

        public enum Mode { DryRun, ApplyToCopy, ApplyInPlace }

        // ------------------------------------------------------------------ entry points

        public static void Run()
        {
            int code = 1;
            try
            {
                string asset = Argument("-setupAsset");
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -setupAsset <Assets/...> (an imported model file).");
                bool apply = Array.IndexOf(Environment.GetCommandLineArgs(), "-setupApply") >= 0;
                string copyTo = Argument("-setupCopyTo");
                if (copyTo != null && !apply)
                    throw new ArgumentException("-setupCopyTo only makes sense together with -setupApply.");
                Mode mode = !apply ? Mode.DryRun : copyTo != null ? Mode.ApplyToCopy : Mode.ApplyInPlace;

                float.TryParse(Argument("-setupHeight"), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float height);
                Dictionary<string, object> report = Execute(asset, mode, copyTo, height);
                string outPath = Argument("-setupOut") ??
                                 "Logs/FrostyNeon/setup_" + Path.GetFileNameWithoutExtension(asset) + ".json";
                string written = AvatarPreflight.WriteReport(report, outPath);
                Debug.Log("Frosty Neon setup helper: " + report["mode"] + ", status " + report["status"] + ", report written to " + written);
                code = (string)report["status"] == "fail" ? 1 : 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon setup helper could not run: " + error);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // ------------------------------------------------------------------ the three modes

        public static Dictionary<string, object> Execute(string assetPath, Mode mode, string copyTo = null, float targetHeight = 0f)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter))
                throw new FileNotFoundException("Not an imported model file: " + assetPath);

            var report = new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-setup-helper",
                ["unity_version"] = Application.unityVersion,
                ["mode"] = mode == Mode.DryRun ? "dry_run" : mode == Mode.ApplyToCopy ? "applied_to_copy" : "applied_in_place",
                ["source_asset"] = assetPath,
            };
            Dictionary<string, object> before = AvatarPreflight.AnalyzeAsset(assetPath, null);
            report["preflight_before"] = Summary(before);

            string target = assetPath;
            bool temporary = false;
            if (mode == Mode.DryRun)
            {
                if (!AssetDatabase.IsValidFolder(DryRunFolder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(DryRunFolder).Replace('\\', '/'), Path.GetFileName(DryRunFolder));
                target = DryRunFolder + "/" + Path.GetFileName(assetPath);
                AssetDatabase.DeleteAsset(target);
                temporary = true;
            }
            else if (mode == Mode.ApplyToCopy)
            {
                if (string.IsNullOrEmpty(copyTo) || !copyTo.StartsWith("Assets/"))
                    throw new ArgumentException("The copy path must be inside Assets/: " + copyTo);
                if (AssetImporter.GetAtPath(copyTo) != null || File.Exists(copyTo))
                    throw new IOException("Copy target already exists, not overwriting: " + copyTo);
                target = copyTo;
            }

            try
            {
                if (target != assetPath && !AssetDatabase.CopyAsset(assetPath, target))
                    throw new IOException("Could not copy " + assetPath + " to " + target);

                var changes = new List<object>();
                var problems = new List<object>();
                ApplySettings(target, changes, problems, targetHeight);
                Dictionary<string, object> after = AvatarPreflight.AnalyzeAsset(target, null);

                report["target_asset"] = temporary ? null : target;
                report["settings_changed"] = mode != Mode.DryRun;
                report[mode == Mode.DryRun ? "changes_that_would_be_made" : "changes_made"] = changes;
                report["problems"] = problems;
                if (temporary)
                {
                    after["asset"] = assetPath + " (temporary copy, deleted)";
                    report["preflight_after_is_prediction"] = true;
                }
                report["preflight_after"] = after;
                report["status"] = problems.Count > 0 || (string)after["status"] == "fail" ? "fail" : (string)after["status"];
            }
            finally
            {
                if (temporary) AssetDatabase.DeleteAsset(DryRunFolder);
            }
            return report;
        }

        static Dictionary<string, object> Summary(Dictionary<string, object> preflight)
        {
            return new Dictionary<string, object>
            {
                ["status"] = preflight["status"],
                ["finding_codes"] = ((List<object>)preflight["findings"]).Cast<Dictionary<string, object>>()
                                    .Select(f => f["code"]).ToList(),
            };
        }

        // ------------------------------------------------------------------ the changes

        /// <summary>Changes the import settings of the model at assetPath and records each change.</summary>
        static void ApplySettings(string assetPath, List<object> changes, List<object> problems, float targetHeight)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                Change(changes, "Animation Type", importer.animationType.ToString(), "Human");
                importer.animationType = ModelImporterAnimationType.Human;
                dirty = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                Change(changes, "Avatar Definition", importer.avatarSetup.ToString(), "CreateFromThisModel");
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }
            // Files in other units (most often centimetres) are converted by "Convert Units". Unity leaves the
            // stored humanoid skeleton unconverted, and VRChat uses that skeleton as the rest pose, so the avatar
            // breaks (found 2026-11-15). Moving the conversion into Scale Factor keeps the size and fixes it.
            if (importer.useFileScale && Mathf.Abs(importer.fileScale - 1f) > 1e-6f)
            {
                float before = importer.globalScale;
                string file = importer.fileScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                importer.globalScale = before * importer.fileScale;
                importer.useFileScale = false;
                Change(changes, "Convert Units (file scale " + file + ") moved into Scale Factor",
                       "on, Scale Factor " + before.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       "off, Scale Factor " + importer.globalScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                dirty = true;
            }
            if (!importer.isReadable)
            {
                Change(changes, "Mesh Read/Write", "disabled", "enabled");
                importer.isReadable = true;
                dirty = true;
            }
            if (AvatarPreflight.NeedsLegacyBlendShapeNormals(importer))
            {
                Change(changes, "Legacy Blendshape Normals", "disabled", "enabled");
                AvatarPreflight.EnableLegacyBlendShapeNormals(importer);
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();

            // Size. With a target height: scale the import so the model is that tall. Without one: a model a
            // few centimetres tall whose size times 100 is human-sized was almost certainly exported with its
            // units read as centimetres, so scale it by 100. Anything else is left alone (the preflight warns).
            float height = ModelHeight(assetPath);
            float factor = 1f;
            if (targetHeight > 0f && height > 0f && Mathf.Abs(targetHeight / height - 1f) > 0.02f)
                factor = targetHeight / height;
            else if (targetHeight <= 0f && height > 0f && height < 0.3f && height * 100f >= 0.5f && height * 100f <= 3f)
                factor = 100f;
            if (factor != 1f)
            {
                importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
                float before = importer.globalScale;
                importer.globalScale = before * factor;
                importer.SaveAndReimport();
                Change(changes, "Scale Factor (model was " + Math.Round(height, 3) + " m tall)",
                       before.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       importer.globalScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // If the stored skeleton still disagrees with the model, let Unity rebuild it from the model.
            float stored = StoredSkeletonScale(assetPath);
            if (stored > 0f && Mathf.Abs(stored - 1f) > 0.05f)
            {
                importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
                importer.humanDescription = new HumanDescription();
                importer.SaveAndReimport();
                Change(changes, "Humanoid skeleton record", "x" + Math.Round(stored, 3) + " the model", "rebuilt from the model");
            }

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Problem(problems, "humanoid_avatar_invalid",
                        "Unity could not build a valid humanoid Avatar from this model's skeleton, so no bone mapping " +
                        "was written. The rig may lack bones Unity requires; see the Blender audit's bone report.");
                return;
            }

            HumanDescription description = avatar.humanDescription;
            if (description.human.Any(b => b.humanName == "Chest")) return;

            string candidate, reason;
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), scene);
                var animator = instance.GetComponent<Animator>();
                var used = new HashSet<Transform>();
                foreach (HumanBodyBones slot in Enum.GetValues(typeof(HumanBodyBones)))
                    if (slot != HumanBodyBones.LastBone && animator.GetBoneTransform(slot)) used.Add(animator.GetBoneTransform(slot));
                Transform found = FindChestCandidate(animator.GetBoneTransform(HumanBodyBones.Neck),
                    animator.GetBoneTransform(HumanBodyBones.LeftShoulder), animator.GetBoneTransform(HumanBodyBones.RightShoulder),
                    animator.GetBoneTransform(HumanBodyBones.Spine), used, out reason);
                candidate = found ? found.name : null;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            if (candidate == null)
            {
                Problem(problems, "chest_not_assigned", "The Chest slot is empty and was left empty: " + reason);
                return;
            }
            if (description.skeleton.Count(b => b.name == candidate) != 1)
            {
                Problem(problems, "chest_not_assigned", "The Chest slot is empty and was left empty: the bone name '" +
                        candidate + "' is not unique in the skeleton.");
                return;
            }

            var bones = description.human.ToList();
            bones.Add(new HumanBone { humanName = "Chest", boneName = candidate, limit = new HumanLimit { useDefaultValues = true } });
            description.human = bones.ToArray();
            importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
            importer.humanDescription = description;
            importer.SaveAndReimport();
            Change(changes, "Humanoid bone mapping: Chest", "(empty)", candidate);

            avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman ||
                !avatar.humanDescription.human.Any(b => b.humanName == "Chest" && b.boneName == candidate))
                Problem(problems, "chest_assignment_rejected",
                        "Unity did not accept '" + candidate + "' in the Chest slot; check the Avatar by hand.");
        }

        /// <summary>See AvatarPreflight.StoredSkeletonScale; measured on the imported model.</summary>
        public static float StoredSkeletonScale(string assetPath)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), scene);
                return AvatarPreflight.StoredSkeletonScale(instance);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Height in metres of everything the model renders, as imported. 0 if it renders nothing.</summary>
        public static float ModelHeight(string assetPath)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), scene);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return 0f;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                return bounds.size.y;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>
        /// The one bone that can go in an empty Chest slot under the VRChat SDK's rule (shoulders and neck
        /// are direct children of the Chest): the shared parent of the neck and both shoulders, provided it
        /// is not already used for another humanoid slot and sits below the Spine bone. Null when there is
        /// no such bone or the choice is not clear; `reason` then says why.
        /// </summary>
        public static Transform FindChestCandidate(Transform neck, Transform leftShoulder, Transform rightShoulder,
                                                   Transform spine, ICollection<Transform> usedBones, out string reason)
        {
            reason = null;
            if (!neck || !leftShoulder || !rightShoulder || !spine)
            {
                reason = "neck, both shoulders and spine must be mapped first.";
                return null;
            }
            Transform parent = neck.parent;
            if (!parent || leftShoulder.parent != parent || rightShoulder.parent != parent)
            {
                reason = "the neck and the two shoulders do not share one parent bone, so the chest bone is ambiguous.";
                return null;
            }
            if (usedBones.Contains(parent))
            {
                reason = "the parent of the neck and shoulders ('" + parent.name + "') is already used for another " +
                         "humanoid slot; the rig has no separate chest bone.";
                return null;
            }
            if (!parent.IsChildOf(spine))
            {
                reason = "the parent of the neck and shoulders ('" + parent.name + "') is not below the Spine bone.";
                return null;
            }
            return parent;
        }

        static void Change(List<object> changes, string setting, string before, string after)
        {
            changes.Add(new Dictionary<string, object> { ["setting"] = setting, ["before"] = before, ["after"] = after });
        }

        static void Problem(List<object> problems, string code, string message)
        {
            problems.Add(new Dictionary<string, object> { ["code"] = code, ["message"] = message });
        }
    }
}
