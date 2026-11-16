// Frosty Neon avatar pipeline (Editor-only): runs the Unity-side tools in order for one imported model.
//
//   1. preflight          what VRChat's SDK would say about the model as imported
//   2. import settings    AvatarSetupHelper  (humanoid, Chest slot, mesh Read/Write, legacy blend shape normals)
//   3. materials          AvatarMaterialSetup (one VRChat/Mobile material set for PC and Quest)
//   4. avatar prefab      AvatarDescriptorSetup (<model>_Avatar.prefab with descriptor, view position, visemes)
//   5. preview            AvatarPreviewRender (front, three-quarter and back PNG files)
//   6. final preflight    on the avatar prefab
//
// Without -pipelineApply it is a dry run: each step reports what it would do and nothing in the project
// changes. With -pipelineApply, steps 2 to 4 change the model's import settings and create material
// and prefab assets next to it; the model file itself is never modified. Nothing is uploaded: uploading is
// separate (frosty.ps1 upload or the Avatar Guide; docs/AVATAR_UPLOAD.md).
//
// Batch mode (Editor closed):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarPipeline.Run
//       -pipelineAsset Assets/Path/Avatar.fbx [-pipelineApply] [-pipelineHeight <metres>] [-pipelineOut report.json] -logFile <log>
//   -pipelineHeight: the avatar's intended height; the import is scaled to match (see AvatarSetupHelper).
//   Exit code 0 = the final preflight is pass or warn; 1 = fail, a step reported a problem, or it could not run.
// In the Editor: Frosty Neon > Avatar Pipeline (window). See docs/AVATAR_PIPELINE.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarPipeline
    {
        public static void Run()
        {
            int code = 1;
            string asset = Argument("-pipelineAsset");
            try
            {
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -pipelineAsset <Assets/...> (an imported model file).");
                bool apply = Array.IndexOf(Environment.GetCommandLineArgs(), "-pipelineApply") >= 0;
                float.TryParse(Argument("-pipelineHeight"), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float height);
                Dictionary<string, object> report = Execute(asset, apply, "Logs/FrostyNeon/previews", height);
                string written = AvatarPreflight.WriteReport(report, Argument("-pipelineOut") ??
                    "Logs/FrostyNeon/pipeline_" + Path.GetFileNameWithoutExtension(asset) + ".json");
                Debug.Log("Frosty Neon pipeline: " + report["mode"] + ", status " + report["status"] + "\n" + report["summary"] +
                          "\nReport written to " + written);
                code = (string)report["status"] == "fail" ? 1 : 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon pipeline could not run: " + error);
                // A report all the same, so frosty.ps1 can show the reason instead of pointing at the Unity log.
                if (!string.IsNullOrEmpty(asset))
                    try { AvatarPreflight.WriteReport(new Dictionary<string, object>
                    {
                        ["tool"] = "frosty-neon-unity-avatar-pipeline",
                        ["asset"] = asset,
                        ["status"] = "fail",
                        ["summary"] = "Could not run: " + error.Message,
                        ["next"] = "Check the path: it must be a model file inside the Unity project, for example Assets/FrostyNeon/Models/Me/Me.fbx.",
                    }, Argument("-pipelineOut") ?? "Logs/FrostyNeon/pipeline_" + Path.GetFileNameWithoutExtension(asset) + ".json"); }
                    catch (Exception reportError) { Debug.LogError("Frosty Neon pipeline: could not write the report either: " + reportError.Message); }
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        /// <summary>Runs the steps and returns one report. Each step's full report is included under "steps".</summary>
        public static Dictionary<string, object> Execute(string assetPath, bool apply, string previewDir = "Logs/FrostyNeon/previews",
                                                         float targetHeight = 0f)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter))
                throw new FileNotFoundException("Not an imported model file: " + assetPath);
            var steps = new List<object>();
            var lines = new List<string>();
            bool problem = false;

            void Step(string name, Func<Dictionary<string, object>> run, Func<Dictionary<string, object>, string> say)
            {
                var row = new Dictionary<string, object> { ["step"] = name };
                try
                {
                    Dictionary<string, object> result = run();
                    row["status"] = result.ContainsKey("status") ? result["status"] : "done";
                    row["summary"] = say(result);
                    row["report"] = result;
                    if (result.ContainsKey("problems") && ((List<object>)result["problems"]).Count > 0) problem = true;
                }
                catch (Exception error)
                {
                    row["status"] = "error";
                    row["summary"] = error.Message;
                    problem = true;
                }
                steps.Add(row);
                lines.Add(name + ": " + row["summary"]);
            }

            Step("1 preflight (as imported)", () => AvatarPreflight.AnalyzeAsset(assetPath, null), Findings);
            Step("2 import settings", () => AvatarSetupHelper.Execute(assetPath,
                    apply ? AvatarSetupHelper.Mode.ApplyInPlace : AvatarSetupHelper.Mode.DryRun, null, targetHeight),
                 r => Changes(r, apply ? "changes_made" : "changes_that_would_be_made", apply));
            Step("3 materials", () => AvatarMaterialSetup.Execute(assetPath, AvatarMaterialSetup.DefaultShader, apply),
                 r => (r.ContainsKey("note") ? ((string)r["note"]).TrimEnd('.') :
                      ((List<object>)r[apply ? "materials" : "materials_that_would_be_set_up"]).Count + " material(s) " +
                      (apply ? "set up" : "would be set up") + " with " + r["shader"]) +
                      TextureNote(r, apply));
            string prefab = AvatarDescriptorSetup.DefaultPrefabPath(assetPath);
            Step("4 avatar prefab", () => apply || ((ModelImporter)AssetImporter.GetAtPath(assetPath)).animationType == ModelImporterAnimationType.Human
                     ? AvatarDescriptorSetup.Execute(assetPath, prefab, apply)
                     : new Dictionary<string, object> { ["status"] = "skipped", ["note"] = "Would be created after step 2 makes the model humanoid." },
                 r => r.ContainsKey("note") && !r.ContainsKey("prefab") ? (string)r["note"] :
                      ((bool)r["prefab_existed"] ? "already there, left as it is: " : apply ? "created " : "would create ") + prefab +
                      "; lip sync: " + ((Dictionary<string, object>)r["lip_sync"])["note"]);
            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(prefab) != null;
            string viewTarget = prefabExists ? prefab : assetPath;
            Step("5 preview", () => new Dictionary<string, object>
                 {
                     ["images"] = AvatarPreviewRender.Render(viewTarget, previewDir, 1024).Cast<object>().ToList(),
                 },
                 r => ((List<object>)r["images"]).Count + " images in " + previewDir);
            Dictionary<string, object> final = null;
            Step("6 final preflight", () => final = AvatarPreflight.AnalyzeAsset(viewTarget, null),
                 r => (prefabExists ? "avatar prefab: " : "model (no prefab yet): ") + Findings(r) +
                      (apply ? "" : " (dry run: this is the model as it is now, before any changes)"));

            string status = final == null || problem ? "fail" : (string)final["status"];
            return new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-pipeline",
                ["unity_version"] = Application.unityVersion,
                ["mode"] = apply ? "applied" : "dry_run",
                ["asset"] = assetPath,
                ["avatar_prefab"] = prefabExists ? prefab : null,
                ["status"] = status,
                ["summary"] = string.Join("\n", lines),
                ["steps"] = steps,
                ["next"] = !apply ? "Run it for real to make the changes: frosty.ps1 prepare " + assetPath + " (or Prepare in the window)." :
                           status == "fail" ? "Fix what the final preflight lists; see docs/AVATAR_PREFLIGHT.md." :
                           "Look at the preview images, then upload it: frosty.ps1 upload " + assetPath + ", or the Avatar Guide (docs/AVATAR_UPLOAD.md).",
            };
        }

        static string TextureNote(Dictionary<string, object> report, bool apply)
        {
            string key = apply ? "textures_streaming_enabled" : "textures_that_would_get_streaming";
            int count = report.ContainsKey(key) ? ((List<object>)report[key]).Count : 0;
            return count == 0 ? "" : "; streaming mipmaps " + (apply ? "turned on for " : "would be turned on for ") + count + " texture(s)";
        }

        static string Findings(Dictionary<string, object> preflight)
        {
            var findings = ((List<object>)preflight["findings"]).Cast<Dictionary<string, object>>().ToList();
            var platforms = (Dictionary<string, object>)preflight["platforms"];
            string ranks = string.Join(", ", platforms.Select(p => p.Key + " " + ((Dictionary<string, object>)p.Value)["sdk_overall_rank"]));
            return preflight["status"] + " (" + ranks + ")" +
                   (findings.Count == 0 ? ", no findings" : ", findings: " + string.Join(", ", findings.Select(f => f["code"])));
        }

        static string Changes(Dictionary<string, object> report, string key, bool apply)
        {
            var changes = ((List<object>)report[key]).Cast<Dictionary<string, object>>().ToList();
            var problems = ((List<object>)report["problems"]).Cast<Dictionary<string, object>>().ToList();
            string text = changes.Count == 0 ? "nothing to change" :
                (apply ? "changed " : "would change ") + string.Join(", ", changes.Select(c => c["setting"]));
            return problems.Count == 0 ? text : text + "; problems: " + string.Join(", ", problems.Select(p => p["code"]));
        }
    }
}
