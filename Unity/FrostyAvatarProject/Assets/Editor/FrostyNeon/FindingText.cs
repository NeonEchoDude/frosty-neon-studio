// Plain-language text for every preflight finding (stage 2, docs/STAGE2_PLAN.md): what it means for the person
// wearing the avatar, in words for someone who has never used Unity. The technical message stays in the report.
// FindingTextSelfTest checks that every code the preflight can produce has an entry here.

using System.Collections.Generic;
using System.Linq;

namespace FrostyNeon.Editor
{
    public static class FindingText
    {
        public const string MustFix = "Must fix before uploading";
        public const string FixedForYou = "Fixed for you by the Fix step";
        public const string GoodToKnow = "Good to know";

        public struct Text
        {
            public string Title;
            public string Meaning;
            public Text(string title, string meaning) { Title = title; Meaning = meaning; }
        }

        /// <summary>Codes the Fix step (Prepare) repairs on a plain model. For a creator's ready prefab see AvatarGuidedFix.Fixes.</summary>
        public static readonly HashSet<string> FixableOnModel = new HashSet<string>
        {
            "mesh_read_write_disabled", "sdk_legacy_blend_shape_normals", "sdk_texture_streaming_mipmaps",
            "humanoid_rest_pose_scale_mismatch", "sdk_no_animator", "sdk_not_humanoid", "sdk_quest_unsupported_shader",
        };

        public static readonly Dictionary<string, Text> Texts = new Dictionary<string, Text>
        {
            // Errors
            ["humanoid_rest_pose_scale_mismatch"] = new Text("Skeleton stored at the wrong size",
                "Unity saved the avatar's skeleton at a different size from the model. In VRChat the avatar would be broken, for example standing in the floor."),
            ["mesh_read_write_disabled"] = new Text("Meshes locked against reading",
                "A setting stops VRChat's tools from reading the avatar's meshes, so the upload would fail."),
            ["no_renderers"] = new Text("Nothing visible",
                "No meshes were found, so there is nothing to see. Check that you chose the avatar itself and not an empty object."),
            ["sdk_feet_not_specified"] = new Text("Feet not found in the skeleton",
                "VRChat needs to know which bones are the feet to make the avatar walk."),
            ["sdk_head_hands_feet_unmapped"] = new Text("Head, hands or feet not found in the skeleton",
                "VRChat needs these bones to move the avatar with you."),
            ["sdk_legacy_blend_shape_normals"] = new Text("Import setting makes the avatar too big to download",
                "A mesh setting makes the upload much larger; VRChat refuses it until the setting is changed."),
            ["sdk_quest_unsupported_shader"] = new Text("Materials Quest cannot show",
                "Some materials use a shader made for PC. VRChat will not accept the Quest version until they use a Quest shader. The PC version is not affected."),
            ["sdk_spine_hierarchy_incorrect"] = new Text("Spine connected the wrong way",
                "The spine, chest, neck and shoulders are not linked the way VRChat expects, so the body may bend wrongly."),
            ["sdk_spine_missing_elements"] = new Text("Part of the spine not found in the skeleton",
                "The hips, spine, chest, neck or shoulders are not all assigned, so VRChat cannot move the body properly."),
            ["sdk_texture_streaming_mipmaps"] = new Text("Texture setting VRChat requires is off",
                "Some textures have 'Streaming Mip Maps' turned off. VRChat stops the upload until it is on."),
            ["sdk_texture_too_large"] = new Text("Textures too large",
                "Some textures are bigger than VRChat allows (8192 pixels). They need to be made smaller."),
            ["sdk_too_short"] = new Text("Avatar far too small",
                "The shoulders are under 20 cm high, so VRChat refuses it. Usually the model was saved in the wrong units; set a height when fixing it."),
            ["sdk_upper_arms_not_specified"] = new Text("Upper arms not found in the skeleton",
                "VRChat needs the upper arms to move the avatar's arms with yours."),
            ["sdk_viseme_face_mesh_missing"] = new Text("Lip sync has no face to move",
                "Mouth movement is switched on but no face mesh is chosen, so VRChat refuses the upload."),
            ["material_shader_missing"] = new Text("Shader not installed (pink materials)",
                "Some materials need a shader that is not in this project, so they show pink. Add the shader named in the details with VRChat Creator Companion, then check again."),
            // Warnings
            ["blend_shape_default_weight_nonzero"] = new Text("A face shape is switched on",
                "A face shape (for example a mouth shape) is partly on by default, so the face may look odd at rest."),
            ["missing_material"] = new Text("Empty material slot",
                "Part of the avatar has no material, so it may show pink or not at all."),
            ["model_very_small"] = new Text("Model very small",
                "The model is under 30 cm tall, which usually means it was saved in the wrong units. Set the intended height when fixing it."),
            ["non_unit_scale"] = new Text("Part of the model is scaled",
                "Part of the model has a size setting other than 1. Usually harmless; if the avatar looks the wrong size in VRChat, this is a place to look."),
            ["performance_below_target"] = new Text("Heavier than the performance target",
                "The avatar uses more than the target allows, so some players may see it hidden or replaced until they choose to show it."),
            ["renderer_without_mesh"] = new Text("Empty mesh slot",
                "An object is set up to show a mesh but has none. It does nothing and can be ignored."),
            ["sdk_arm_first_child_order"] = new Text("Unusual bone order in an arm",
                "VRChat's body tracking may behave slightly oddly for that arm. Usually fine."),
            ["sdk_leg_first_child_order"] = new Text("Unusual bone order in a leg",
                "VRChat's body tracking may behave slightly oddly for that leg. Usually fine."),
            ["sdk_feet_below_origin"] = new Text("Feet below the floor point",
                "The feet are lower than the avatar's floor point, so it may stand partly in the floor."),
            ["sdk_no_animator"] = new Text("Not set up to move yet",
                "The model is not set up as a body that can walk and move in VRChat."),
            ["sdk_not_humanoid"] = new Text("Not set up as a humanoid yet",
                "The model is not set up as a human-shaped body, so VRChat's walking and tracking would not work."),
            ["sdk_shorter_than_average"] = new Text("Shorter than average",
                "Fine if that is how the avatar is meant to be."),
            ["sdk_taller_than_average"] = new Text("Taller than average",
                "Fine if that is how the avatar is meant to be."),
            ["sdk_too_tall"] = new Text("Very tall",
                "The shoulders are over 5 m high. VRChat may not handle it well; check the size is what you intended."),
            ["sdk_split_hierarchy"] = new Text("Skeleton split into separate parts",
                "Parts of the skeleton sit in different places in the model, which can make the avatar behave oddly."),
            ["sdk_version_differs"] = new Text("Different VRChat SDK version",
                "This project has a different VRChat SDK version from the one the toolkit was written for, so some checks may be slightly off."),
        };

        /// <summary>
        /// Which of the three groups a finding goes in. isModel: a plain model, which Prepare fixes; otherwise an avatar
        /// prefab, for which the Guided Fix repairs import settings (AvatarGuidedFix.Fixes).
        /// </summary>
        public static string Group(Dictionary<string, object> finding, bool isModel)
        {
            string code = (string)finding["code"];
            if ((isModel ? FixableOnModel : AvatarGuidedFix.Fixes).Contains(code)) return FixedForYou;
            if (!isModel && code == "sdk_quest_unsupported_shader") return FixedForYou;   // by making a Quest version (AvatarQuestVersion)
            return (string)finding["severity"] == "error" ? MustFix : GoodToKnow;
        }

        public static Text For(Dictionary<string, object> finding)
        {
            string code = (string)finding["code"];
            return Texts.TryGetValue(code, out Text text) ? text : new Text(code.Replace('_', ' '), (string)finding["message"]);
        }

        /// <summary>A preflight report's findings sorted into the three groups, in a fixed order.</summary>
        public static List<KeyValuePair<string, List<Dictionary<string, object>>>> Sort(Dictionary<string, object> report, bool isModel)
        {
            var findings = ((List<object>)report["findings"]).Cast<Dictionary<string, object>>().ToList();
            return new[] { MustFix, FixedForYou, GoodToKnow }
                .Select(group => new KeyValuePair<string, List<Dictionary<string, object>>>(group,
                    findings.Where(f => Group(f, isModel) == group).ToList()))
                .ToList();
        }

        /// <summary>One line on whether the avatar can go up, per platform, in plain words.</summary>
        public static string PlatformLine(Dictionary<string, object> report, string platform)
        {
            var status = (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])[platform])["status"];
            string name = platform == "pc" ? "PC" : "Quest";
            return status == "pass" ? name + ": ready."
                 : status == "warn" ? name + ": ready, with notes below."
                 : name + ": not ready yet; see what to fix below.";
        }
    }
}
