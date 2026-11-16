// Frosty Neon avatar pipeline window (Editor-only): one place to run the toolkit on a model.
// Menu: Frosty Neon > Avatar Pipeline.
//
// Pick an imported model (FBX) and press:
//   Check     dry run of the whole pipeline; changes nothing
//   Prepare   applies the import settings, materials and avatar prefab, renders previews, re-checks
//   Upload    uploads the avatar prefab to the signed-in VRChat account as a private avatar (PC or Quest)
// The same steps run from a shell with AvatarPipeline.Run; see docs/AVATAR_PIPELINE.md.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public class FrostyNeonWindow : EditorWindow
    {
        GameObject _model;
        string _avatarName = "";
        float _height;
        string _summary = "Pick a model and press Check.";
        string _status = "";
        Vector2 _scroll;
        readonly List<Texture2D> _previews = new List<Texture2D>();

        [MenuItem("Frosty Neon/Avatar Pipeline", false, 0)]
        public static void Open()
        {
            GetWindow<FrostyNeonWindow>("Frosty Neon");
        }

        void OnGUI()
        {
            FrostyTheme.Header("FROSTY NEON  ·  AVATAR PIPELINE", "Check, prepare and upload a model: the power-user view.");
            if (GUILayout.Button("New here? Open the Avatar Guide for step-by-step help.", EditorStyles.linkLabel)) FrostyNeonGuide.Open();
            EditorGUILayout.HelpBox("Pick the imported model (the .fbx in the Project window). The model file itself is never " +
                                    "changed. Check changes nothing; Prepare changes its import settings and adds a material " +
                                    "folder and an avatar prefab next to it.", MessageType.None);

            var picked = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), false);
            if (picked != _model)
            {
                _model = picked;
                _avatarName = _model ? _model.name : "";
                _previews.Clear();
            }
            string path = _model ? AssetDatabase.GetAssetPath(_model) : null;
            bool isModel = path != null && AssetImporter.GetAtPath(path) is ModelImporter;
            if (_model && !isModel)
                EditorGUILayout.HelpBox("That is not an imported model file. Pick the .fbx itself.", MessageType.Warning);

            _height = EditorGUILayout.FloatField(new GUIContent("Height (m, optional)",
                "The avatar's intended height. Leave 0 to keep its size; Prepare scales the import to this height."), _height);
            using (new EditorGUI.DisabledScope(!isModel))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check", FrostyTheme.ButtonAlt)) RunPipeline(path, false);
                if (GUILayout.Button("Prepare", FrostyTheme.Button) && EditorUtility.DisplayDialog("Frosty Neon",
                        "Prepare " + _model.name + "?\n\nThis changes the model's import settings and creates a Materials folder " +
                        "and " + _model.name + "_Avatar.prefab next to it. Existing materials and prefabs are kept as they are.",
                        "Prepare", "Cancel"))
                    RunPipeline(path, true);
            }

            string prefab = isModel ? AvatarDescriptorSetup.DefaultPrefabPath(path) : null;
            bool hasPrefab = prefab != null && AssetDatabase.LoadAssetAtPath<GameObject>(prefab) != null;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Upload (private, to the account signed in to the VRChat SDK)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!hasPrefab))
            {
                _avatarName = EditorGUILayout.TextField("Avatar name", _avatarName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Upload PC", FrostyTheme.Button)) AvatarUpload.Upload(prefab, _avatarName, false);
                    if (GUILayout.Button("Upload Quest", FrostyTheme.ButtonAlt)) AvatarUpload.Upload(prefab, _avatarName, true);
                }
            }
            if (isModel && !hasPrefab)
                EditorGUILayout.HelpBox("Upload needs the avatar prefab; press Prepare first.", MessageType.Info);

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox("Status: " + _status, _status == "fail" ? MessageType.Error :
                                        _status == "warn" ? MessageType.Warning : MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_summary, EditorStyles.wordWrappedLabel);
            if (_previews.Count > 0)
                using (new EditorGUILayout.HorizontalScope())
                    foreach (Texture2D image in _previews)
                        GUILayout.Label(image, GUILayout.Width(160), GUILayout.Height(160));
            EditorGUILayout.EndScrollView();
        }

        void RunPipeline(string path, bool apply)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Frosty Neon", apply ? "Preparing the avatar..." : "Checking the avatar...", 0.5f);
                Dictionary<string, object> report = AvatarPipeline.Execute(path, apply, "Logs/FrostyNeon/previews", _height);
                string written = AvatarPreflight.WriteReport(report,
                    "Logs/FrostyNeon/pipeline_" + System.IO.Path.GetFileNameWithoutExtension(path) + ".json");
                _status = (string)report["status"];
                _summary = report["summary"] + "\n\nNext: " + report["next"] + "\nFull report: " + written;
                LoadPreviews(report);
            }
            catch (Exception error)
            {
                _status = "fail";
                _summary = "Could not run: " + error.Message;
                Debug.LogException(error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void LoadPreviews(Dictionary<string, object> report)
        {
            _previews.Clear();
            var step = ((List<object>)report["steps"]).Cast<Dictionary<string, object>>()
                .FirstOrDefault(s => ((string)s["step"]).StartsWith("5") && s.ContainsKey("report"));
            if (step == null) return;
            foreach (string file in ((List<object>)((Dictionary<string, object>)step["report"])["images"]).Cast<string>())
            {
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(System.IO.File.ReadAllBytes(file))) _previews.Add(texture);
            }
        }
    }
}
