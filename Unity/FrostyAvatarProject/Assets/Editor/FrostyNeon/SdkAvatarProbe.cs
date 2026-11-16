// Editor-only measurement tool that asks the installed VRChat SDK about the test fixtures.
// Written against VRChat SDK 3.10.6 (com.vrchat.base / com.vrchat.avatars); re-check the calls
// below against the SDK source if the SDK is updated.
//
// For each fixture it places a temporary copy in an empty scene and reports:
//   * performance: the SDK's own AvatarPerformance.CalculatePerformanceStats result, for the
//     PC limits and for the mobile (Quest) limits. This is the calculation behind the build
//     panel's performance section.
//   * rig_conditions: the conditions of the SDK build panel's humanoid rig check
//     (VRCSdkControlPanelAvatarBuilder.AnalyzeIK, SDK 3.10.6), evaluated again here. That method
//     is private and draws its messages into the panel, so this is a re-evaluation of the same
//     conditions, not a call into the panel.
// Nothing is saved to the project.
//
// Run with the Editor closed:
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.SdkAvatarProbe.Run -logFile <log>
// Or from the menu: Frosty Neon > Developer > Probe Fixtures With VRChat SDK.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace FrostyNeon.Editor
{
    public static class SdkAvatarProbe
    {
        const string OutPath = "Logs/FrostyNeon/sdk_avatar_probe.json";

        static readonly string[] Fixtures =
        {
            "Assets/FrostyNeon/Fixtures/fbx_fixture_v1.fbx",
            "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1.fbx",
            "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_explicit_chest.fbx",
        };

        [MenuItem("Frosty Neon/Developer/Probe Fixtures With VRChat SDK", false, 2000)]
        public static void RunFromMenu()
        {
            Debug.Log("Frosty Neon SDK probe written to " + Measure());
        }

        public static void Run()
        {
            int code = 0;
            try
            {
                Debug.Log("Frosty Neon SDK probe written to " + Measure());
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon SDK probe failed: " + error);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static string Measure()
        {
            var json = new StringBuilder();
            json.Append("{\n  \"tool\": \"frosty-neon-unity-sdk-avatar-probe\",\n");
            json.Append("  \"unity_version\": ").Append(Quote(Application.unityVersion)).Append(",\n");
            json.Append("  \"build_target\": ").Append(Quote(EditorUserBuildSettings.activeBuildTarget.ToString())).Append(",\n");
            json.Append("  \"fixtures\": {\n");

            var blocks = new List<string>();
            foreach (string path in Fixtures)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    blocks.Add("    " + Quote(path) + ": {\"error\": \"not found\"}");
                    continue;
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var block = new StringBuilder();
                block.Append("    ").Append(Quote(path)).Append(": {\n");
                block.Append("      \"performance\": {\n");
                block.Append("        \"pc\": ").Append(Performance(instance, false)).Append(",\n");
                block.Append("        \"mobile\": ").Append(Performance(instance, true)).Append("\n      },\n");
                block.Append("      \"rig_conditions\": ").Append(RigConditions(instance)).Append("\n    }");
                blocks.Add(block.ToString());
                UnityEngine.Object.DestroyImmediate(instance);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            json.Append(string.Join(",\n", blocks)).Append("\n  }\n}\n");

            string fullPath = Path.GetFullPath(OutPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, json.ToString(), new UTF8Encoding(false));
            return fullPath;
        }

        // The SDK's own calculation. Every rating and every simple statistic value is written out
        // by name, so nothing depends on this script knowing the SDK's field list.
        static string Performance(GameObject avatar, bool mobile)
        {
            var stats = new AvatarPerformanceStats(mobile);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, stats, mobile);

            var ratings = new List<string>();
            foreach (AvatarPerformanceCategory category in Enum.GetValues(typeof(AvatarPerformanceCategory)))
            {
                if (category == AvatarPerformanceCategory.AvatarPerformanceCategoryCount) continue;
                string rating;
                try { rating = stats.GetPerformanceRatingForCategory(category).ToString(); }
                catch (Exception error) { rating = "error: " + error.GetType().Name; }
                ratings.Add("          " + Quote(category.ToString()) + ": " + Quote(rating));
            }

            var values = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(AvatarPerformanceStats).GetFields(flags))
                AddValue(values, field.Name, field.FieldType, () => field.GetValue(stats));
            foreach (PropertyInfo property in typeof(AvatarPerformanceStats).GetProperties(flags))
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                    AddValue(values, property.Name, property.PropertyType, () => property.GetValue(stats));

            return "{\n        \"ratings\": {\n" + string.Join(",\n", ratings) + "\n        },\n" +
                   "        \"values\": {\n" + string.Join(",\n", values) + "\n        }\n      }";
        }

        static void AddValue(List<string> values, string name, Type type, Func<object> read)
        {
            Type inner = Nullable.GetUnderlyingType(type) ?? type;
            if (!(inner.IsPrimitive || inner.IsEnum || inner == typeof(string) || inner == typeof(Bounds))) return;
            object value;
            try { value = read(); }
            catch (Exception error) { value = "error: " + error.GetType().Name; }
            string text = value is Bounds bounds ? "size " + bounds.size.ToString("F3") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            values.Add("          " + Quote(name) + ": " + Quote(value == null ? null : text));
        }

        // Same conditions, in the same order, as VRCSdkControlPanelAvatarBuilder.AnalyzeIK in SDK 3.10.6.
        static string RigConditions(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            bool human = animator != null && animator.isHuman;
            var rows = new List<string> { Row("is_humanoid", human) };
            if (!human)
            {
                rows.Add(Row("sdk_message", "This avatar is not imported as a humanoid rig and will not play VRChat's provided animation set."));
                return "{\n" + string.Join(",\n", rows) + "\n      }";
            }

            Func<HumanBodyBones, Transform> bone = animator.GetBoneTransform;
            Transform head = bone(HumanBodyBones.Head), lFoot = bone(HumanBodyBones.LeftFoot), rFoot = bone(HumanBodyBones.RightFoot);
            Transform lHand = bone(HumanBodyBones.LeftHand), rHand = bone(HumanBodyBones.RightHand);
            bool essentials = head && lFoot && rFoot && lHand && rHand;
            rows.Add(Row("head_hands_feet_mapped", essentials));
            var messages = new List<string>();
            if (!essentials) messages.Add("error: Humanoid avatar must have head, hands and feet bones mapped.");

            Transform pelvis = bone(HumanBodyBones.Hips), torso = bone(HumanBodyBones.Spine), chest = bone(HumanBodyBones.Chest);
            Transform upperChest = bone(HumanBodyBones.UpperChest), neck = bone(HumanBodyBones.Neck);
            Transform lClav = bone(HumanBodyBones.LeftShoulder), rClav = bone(HumanBodyBones.RightShoulder);
            var missing = new List<string>();
            if (!neck) missing.Add("Neck");
            if (!lClav || !rClav) missing.Add("Shoulders");
            if (!pelvis) missing.Add("Pelvis");
            if (!torso) missing.Add("Spine");
            if (!chest) missing.Add("Chest");
            rows.Add("        \"spine_elements_missing\": [" + string.Join(", ", missing.Select(Quote)) + "]");
            rows.Add(Row("upper_chest_mapped", (bool)upperChest));

            if (essentials && missing.Count > 0)
                messages.Add("error: Spine hierarchy missing elements, please map: " + string.Join(", ", missing) + ".");
            if (essentials && missing.Count == 0)
            {
                Transform expectedParent = upperChest ? upperChest : chest;
                bool spineOk = lClav.parent == expectedParent && rClav.parent == expectedParent && neck.parent == expectedParent;
                rows.Add(Row("shoulders_and_neck_parent_expected", expectedParent.name));
                rows.Add(Row("spine_hierarchy_correct", spineOk));
                if (!spineOk)
                    messages.Add("error: Spine hierarchy incorrect. Make sure that the parent of both Shoulders and the Neck is the Chest (or UpperChest if set).");
                else
                {
                    Transform lUpper = bone(HumanBodyBones.LeftUpperArm), lLower = bone(HumanBodyBones.LeftLowerArm);
                    Transform rUpper = bone(HumanBodyBones.RightUpperArm), rLower = bone(HumanBodyBones.RightLowerArm);
                    bool arms = lUpper && lLower && lUpper.GetChild(0) == lLower && lLower.GetChild(0) == lHand
                                && rUpper && rLower && rUpper.GetChild(0) == rLower && rLower.GetChild(0) == rHand;
                    Transform lHip = bone(HumanBodyBones.LeftUpperLeg), lKnee = bone(HumanBodyBones.LeftLowerLeg);
                    Transform rHip = bone(HumanBodyBones.RightUpperLeg), rKnee = bone(HumanBodyBones.RightLowerLeg);
                    bool legs = lHip && lKnee && lHip.GetChild(0) == lKnee && lKnee.GetChild(0) == lFoot
                                && rHip && rKnee && rHip.GetChild(0) == rKnee && rKnee.GetChild(0) == rFoot;
                    bool joined = lFoot.IsChildOf(pelvis) && rFoot.IsChildOf(pelvis) && lHand.IsChildOf(pelvis) && rHand.IsChildOf(pelvis);
                    rows.Add(Row("arm_first_child_order_correct", arms));
                    rows.Add(Row("leg_first_child_order_correct", legs));
                    rows.Add(Row("hips_ancestor_of_hands_and_feet", joined));
                    if (!arms) messages.Add("warning: LowerArm is not first child of UpperArm or Hand is not first child of LowerArm: you may have problems with Forearm rotations.");
                    if (!legs) messages.Add("warning: LowerLeg is not first child of UpperLeg or Foot is not first child of LowerLeg: you may have problems with Shin rotations.");
                    if (!joined) messages.Add("warning: This avatar has a split hierarchy (Hips bone is not the ancestor of all humanoid bones). IK may not work correctly.");
                }
            }
            rows.Add("        \"sdk_messages_expected\": [" + string.Join(", ", messages.Select(Quote)) + "]");
            return "{\n" + string.Join(",\n", rows) + "\n      }";
        }

        static string Row(string key, bool value)
        {
            return "        " + Quote(key) + ": " + (value ? "true" : "false");
        }

        static string Row(string key, string value)
        {
            return "        " + Quote(key) + ": " + Quote(value);
        }

        static string Quote(string text)
        {
            return text == null ? "null" : "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
