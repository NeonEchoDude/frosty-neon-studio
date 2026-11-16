// Batch-mode entry points for the Avatar Guide's actions, so everything the window does can also be run from the shell
// (frosty.ps1 fix / quest / vroid) and, later, from a standalone app (docs/STAGE2_PLAN.md, milestone 4). Each writes a
// JSON report to Logs/FrostyNeon/ and exits 0 when it did what was asked, 1 otherwise. Nothing is uploaded.
//
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.FrostyNeonCommands.Fix
//       -fixPrefab Assets/.../Avatar.prefab [-fixDryRun | -fixUndo] -logFile <log>          -> fix_result.json
//   ... FrostyNeon.Editor.FrostyNeonCommands.Quest -questPrefab Assets/.../Avatar.prefab [-questRemove]  -> quest_result.json
//   ... FrostyNeon.Editor.FrostyNeonCommands.Vroid -vroidFile C:\path\avatar.vrm [-vroidAcceptTerms]     -> vroid_result.json
//   ... FrostyNeon.Editor.FrostyNeonCommands.List                                                       -> list_result.json
//   ... FrostyNeon.Editor.FrostyNeonCommands.Inspect -inspectPrefab Assets/.../Avatar.prefab [-inspectNoPictures]
//                                                                                                       -> inspect_result.json
//       List and Inspect are what Frosty Neon Studio (tools/app) shows: the avatars in the project, and for one avatar
//       the check in plain words (FindingText), the Fix plan, the Quest version, height and eye level, the creator's
//       terms, the last upload and preview pictures (Logs/FrostyNeon/app_previews). They change nothing.
//       Reads the file's VRM version and the creator's terms first; a VRM 1.0 file is not imported, and a file whose
//       terms prohibit changes is only set up with -vroidAcceptTerms. "Only the author" is checked at upload.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class FrostyNeonCommands
    {
        // ------------------------------------------------------------------ Guided Fix
        public static void Fix()
        {
            const string output = "Logs/FrostyNeon/fix_result.json";
            var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-guided-fix" };
            try
            {
                string prefab = RequirePrefab("-fixPrefab", report);
                if (Has("-fixUndo"))
                {
                    report["mode"] = "undo";
                    int restored = AvatarGuidedFix.Undo(prefab);
                    report["restored"] = restored;
                    Statuses(prefab, report);
                    Done(report, output, restored > 0 ? "Put back " + restored + " import setting(s)." : "There was no Fix to undo for this avatar.", 0);
                    return;
                }
                List<AvatarGuidedFix.Change> plan = AvatarGuidedFix.Plan(prefab);
                report["planned"] = plan.Select(c => (object)c.ToString()).ToList();
                if (plan.Count == 0 || Has("-fixDryRun"))
                {
                    report["mode"] = plan.Count == 0 ? "nothing_to_do" : "dry_run";
                    Statuses(prefab, report);
                    Done(report, output, plan.Count == 0 ? "Nothing to fix: the import settings VRChat needs are already right."
                                                         : plan.Count + " setting(s) would change. Dry run: nothing was changed.", 0);
                    return;
                }
                AvatarGuidedFix.Log log = AvatarGuidedFix.Apply(prefab);
                var failed = log.changes.Where(c => !c.Made).ToList();
                report["mode"] = "applied";
                report["failed"] = failed.Select(c => (object)(c + ": " + c.note)).ToList();
                report["record"] = AvatarGuidedFix.LogPath(prefab);
                Statuses(prefab, report);
                Done(report, output, failed.Count == 0 ? "Changed " + plan.Count + " import setting(s). Undo with: frosty.ps1 fix <prefab> -Undo"
                                                       : (plan.Count - failed.Count) + " of " + plan.Count + " setting(s) changed; see 'failed'.", failed.Count == 0 ? 0 : 1);
            }
            catch (Exception error) { Fail(report, output, error); }
        }

        // ------------------------------------------------------------------ Quest version
        public static void Quest()
        {
            const string output = "Logs/FrostyNeon/quest_result.json";
            var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-quest-version" };
            try
            {
                string prefab = RequirePrefab("-questPrefab", report);
                if (AvatarQuestVersion.IsQuestVersion(prefab)) throw new ArgumentException("That is already a Quest version; give the avatar it was made from.");
                if (Has("-questRemove"))
                {
                    report["mode"] = "removed";
                    bool had = AvatarQuestVersion.QuestPrefabFor(prefab) != null;
                    report["restored_textures"] = AvatarQuestVersion.Remove(prefab);
                    Done(report, output, had ? "Removed the Quest version and put back its texture settings." : "There was no Quest version to remove.", 0);
                    return;
                }
                if (AvatarQuestVersion.RefusedMaterials(prefab).Count == 0 && AvatarQuestVersion.QuestPrefabFor(prefab) == null)
                {
                    report["mode"] = "nothing_to_do";
                    Statuses(prefab, report);
                    Done(report, output, "Quest already accepts every material on this avatar, so it needs no separate Quest version.", 0);
                    return;
                }
                AvatarQuestVersion.Result made = AvatarQuestVersion.Make(prefab);
                report["mode"] = "made";
                report["quest_prefab"] = made.QuestPrefab;
                report["converted"] = made.Converted.Cast<object>().ToList();
                report["kept"] = made.Kept.Cast<object>().ToList();
                report["see_through"] = made.SeeThrough.Cast<object>().ToList();
                report["cut_out"] = made.CutOut.Cast<object>().ToList();
                report["textures_capped"] = made.Textures.Select(t => (object)t.texture).ToList();
                if (made.IdNote != null) report["avatar_id_note"] = made.IdNote;
                Dictionary<string, object> check = AvatarPreflight.AnalyzeAsset(made.QuestPrefab, null);
                string quest = PlatformStatus(check, "quest");
                report["quest_status"] = quest;
                report["quest_findings"] = ((List<object>)check["findings"]).Cast<Dictionary<string, object>>()
                    .Where(f => !f.ContainsKey("platform") || (string)f["platform"] == "quest").Select(f => (object)(f["severity"] + " " + f["code"])).ToList();
                Done(report, output, "Made " + made.QuestPrefab + " (" + made.Converted.Count + " Quest material(s)). Quest check: " + quest +
                                     ". Upload it with: frosty.ps1 upload " + prefab + " -Quest", quest == "fail" ? 1 : 0);
            }
            catch (Exception error) { Fail(report, output, error); }
        }

        // ------------------------------------------------------------------ VRoid
        static string _vrm;
        static double _deadline;
        static Dictionary<string, object> _vroidReport;
        const string VroidOutput = "Logs/FrostyNeon/vroid_result.json";

        public static void Vroid()
        {
            _vroidReport = new Dictionary<string, object> { ["tool"] = "frosty-neon-vroid" };
            try
            {
                string file = Argument("-vroidFile");
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) throw new FileNotFoundException("No .vrm file given or found: " + file);
                AvatarVroid.VrmInfo info = AvatarVroid.ReadInfo(file);
                _vroidReport["vrm_version"] = info.Version;
                _vroidReport["vrm_terms"] = info.Lines().Cast<object>().ToList();
                _vroidReport["vrm_warnings"] = info.Warnings().Cast<object>().ToList();
                if (!info.Readable)
                {
                    _vroidReport["mode"] = "vrm_1_0";
                    Done(_vroidReport, VroidOutput, AvatarVroid.Vrm1Message + " Nothing was imported.", 1);
                    return;
                }
                if (info.ModificationProhibited && !Has("-vroidAcceptTerms"))
                {
                    _vroidReport["mode"] = "terms";
                    Done(_vroidReport, VroidOutput, "Not imported: the creator's terms prohibit changes, and setting it up for VRChat changes it. " +
                                                    "If you are its author or have their permission, run again with -AcceptTerms.", 1);
                    return;
                }
                string uniVrm = AvatarVroid.UniVrm();
                _vroidReport["univrm"] = uniVrm;
                if (uniVrm == null)
                {
                    _vroidReport["mode"] = "needs_univrm";
                    Done(_vroidReport, VroidOutput, AvatarVroid.WhatToDo.Replace("\"Import an avatar package\" in the Avatar Guide", "frosty.ps1 import <file> -AllowScripts")
                                                                           .Replace("the Guide lists them before importing", "the import lists them first")
                                                                           .Replace("choose \"Bring in a VRoid file\" again", "run frosty.ps1 vroid again"), 1);
                    return;
                }
                _vrm = AvatarVroid.CopyIntoProject(file);
                _vroidReport["vrm"] = _vrm;
                _deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.update += WaitForUniVrm;    // UniVRM builds its prefab on a later Editor update
            }
            catch (Exception error) { Fail(_vroidReport, VroidOutput, error); }
        }

        static void WaitForUniVrm()
        {
            string made = AvatarVroid.PrefabFor(_vrm);
            if (made == null && EditorApplication.timeSinceStartup < _deadline) return;
            EditorApplication.update -= WaitForUniVrm;
            try
            {
                if (made == null)
                {
                    _vroidReport["mode"] = "not_read";
                    Done(_vroidReport, VroidOutput, "UniVRM did not make a model from " + _vrm + ". It may be a VRM 1.0 file; export it from VRoid Studio as VRM 0.0 and try again.", 1);
                    return;
                }
                _vroidReport["univrm_prefab"] = made;
                string avatar = AvatarVroid.SetUpForVrchat(made, out string lipSync);
                _vroidReport["mode"] = "set_up";
                _vroidReport["avatar_prefab"] = avatar;
                _vroidReport["lip_sync"] = lipSync;
                Statuses(avatar, _vroidReport);
                bool authorOnly = AvatarVroid.ReadInfo(_vrm).OnlyAuthorMayUse;
                Done(_vroidReport, VroidOutput, "Set up " + avatar + " (" + lipSync + "). Next: frosty.ps1 fix / quest / upload with that prefab, or the Avatar Guide." +
                                                (authorOnly ? " Its terms allow only the author to wear it: the upload will ask you to confirm that is you." : ""), 0);
            }
            catch (Exception error) { Fail(_vroidReport, VroidOutput, error); }
        }

        // ------------------------------------------------------------------ List / Inspect (for the app)
        public static void List()
        {
            const string output = "Logs/FrostyNeon/list_result.json";
            var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-list" };
            try
            {
                var avatars = new List<object>();
                foreach (string prefab in AvatarPackage.FindAvatarPrefabs().Where(p => !AvatarQuestVersion.IsQuestVersion(p)))
                {
                    AvatarVroid.VrmInfo terms = AvatarVroid.TermsFor(prefab);
                    avatars.Add(new Dictionary<string, object>
                    {
                        ["prefab"] = prefab,
                        ["name"] = NameOf(prefab),
                        ["kind"] = terms != null ? "VRoid" : FrostyNeonGuide.ModelFor(prefab) != null ? "built by Frosty Neon" : "ready-made",
                        ["quest_version"] = AvatarQuestVersion.QuestPrefabFor(prefab),
                        ["only_author_may_use"] = terms != null && terms.OnlyAuthorMayUse,
                    });
                }
                report["avatars"] = avatars;
                report["univrm"] = AvatarVroid.UniVrm();
                report["build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString();
                Done(report, output, avatars.Count + " avatar(s) in the project.", 0);
            }
            catch (Exception error) { Fail(report, output, error); }
        }

        public static void Inspect()
        {
            const string output = "Logs/FrostyNeon/inspect_result.json";
            var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-inspect" };
            try
            {
                string prefab = RequirePrefab("-inspectPrefab", report);
                report["name"] = NameOf(prefab);
                Dictionary<string, object> check = AvatarPreflight.AnalyzeAsset(prefab, null);
                string quest = AvatarQuestVersion.QuestPrefabFor(prefab);
                Dictionary<string, object> questCheck = quest != null ? AvatarPreflight.AnalyzeAsset(quest, null) : null;
                string pc = PlatformStatus(check, "pc");
                string questStatus = questCheck != null ? PlatformStatus(questCheck, "quest") : PlatformStatus(check, "quest");
                report["pc_status"] = pc;
                report["quest_status"] = questStatus;
                report["headline"] = (string)check["status"] == "pass" ? "Looking fresh! Nothing to fix."
                                   : (string)check["status"] == "warn" ? "Good to go, with a few notes below."
                                   : pc != "fail" && questStatus != "fail" ? "Ready for PC and Quest!"
                                   : pc != "fail" ? "The PC version is ready! Quest needs a bit more."
                                   : "Almost there! Let's sort out the bits below first.";
                var groups = new List<object>();
                foreach (var group in FindingText.Sort(check, false))
                {
                    if (group.Value.Count == 0) continue;
                    groups.Add(new Dictionary<string, object>
                    {
                        ["group"] = group.Key,
                        ["items"] = group.Value.Select(f =>
                        {
                            FindingText.Text text = FindingText.For(f);
                            return (object)new Dictionary<string, object>
                            {
                                ["code"] = f["code"],
                                ["title"] = text.Title,
                                ["meaning"] = text.Meaning,
                                ["platform"] = f.TryGetValue("platform", out object platform) ? platform : null,
                                ["objects"] = f.TryGetValue("objects", out object objects) ? ((List<object>)objects).Take(6).ToList() : null,
                            };
                        }).ToList(),
                    });
                }
                report["groups"] = groups;
                report["fix_plan"] = AvatarGuidedFix.Plan(prefab).Select(c => (object)c.ToString()).ToList();
                report["fix_can_undo"] = AvatarGuidedFix.CanUndo(prefab);
                report["quest_version"] = quest;
                report["quest_version_needed"] = quest == null && PlatformStatus(check, "quest") == "fail" &&
                    ((List<object>)check["findings"]).Cast<Dictionary<string, object>>().Any(f => (string)f["code"] == "sdk_quest_unsupported_shader");
                if (questCheck != null)
                    report["quest_version_notes"] = ((List<object>)questCheck["findings"]).Cast<Dictionary<string, object>>()
                        .Where(f => (!f.ContainsKey("platform") || (string)f["platform"] == "quest") &&
                                    ((string)f["severity"] == "error" || (string)f["code"] == "performance_below_target"))
                        .Select(f => (object)(FindingText.For(f).Title + ": " + FindingText.For(f).Meaning)).ToList();
                AvatarPreviewRender.Facts facts = AvatarPreviewRender.Measure(prefab);
                report["height_m"] = Math.Round(facts.Height, 2);
                report["eyes_m"] = facts.HasView ? Math.Round(facts.ViewHeight, 2) : (object)null;
                report["floor_notes"] = AvatarPreviewRender.Notes(facts).Cast<object>().ToList();
                AvatarVroid.VrmInfo terms = AvatarVroid.TermsFor(prefab);
                if (terms != null)
                {
                    report["vrm_terms"] = terms.Lines().Cast<object>().ToList();
                    report["only_author_may_use"] = terms.OnlyAuthorMayUse;
                }
                if (File.Exists(AvatarUpload.ResultPath))
                {
                    var last = JsonUtility.FromJson<LastUpload>(File.ReadAllText(AvatarUpload.ResultPath));
                    if (last != null && (last.prefab == prefab || (quest != null && last.prefab == quest)))
                        report["last_upload"] = new Dictionary<string, object> { ["platform"] = last.platform, ["uploaded"] = last.uploaded, ["message"] = last.message };
                }
                if (!Has("-inspectNoPictures"))
                {
                    string dir = Path.GetFullPath("Logs/FrostyNeon/app_previews");
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    report["pictures"] = AvatarPreviewRender.Render(prefab, dir, 512, true).Cast<object>().ToList();
                    if (quest != null) report["quest_pictures"] = AvatarPreviewRender.Render(quest, dir, 512, true).Cast<object>().ToList();
                }
                Done(report, output, (string)report["headline"], 0);
            }
            catch (Exception error) { Fail(report, output, error); }
        }

        [Serializable]
        class LastUpload { public string prefab; public string platform; public bool uploaded; public string message; }

        static string NameOf(string prefab)
        {
            string name = Path.GetFileNameWithoutExtension(prefab);
            return name.EndsWith("_Avatar", StringComparison.Ordinal) ? name.Substring(0, name.Length - "_Avatar".Length) : name;
        }

        // ------------------------------------------------------------------ helpers
        static string RequirePrefab(string argument, Dictionary<string, object> report)
        {
            string prefab = Argument(argument)?.Replace('\\', '/');
            report["prefab"] = prefab;
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith("Assets/") || !prefab.EndsWith(".prefab"))
                throw new ArgumentException("Give the avatar prefab (Assets/....prefab) with " + argument + ".");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null) throw new FileNotFoundException("No prefab at " + prefab);
            return prefab;
        }

        static void Statuses(string prefab, Dictionary<string, object> report)
        {
            Dictionary<string, object> check = AvatarPreflight.AnalyzeAsset(prefab, null);
            report["pc_status"] = PlatformStatus(check, "pc");
            report["quest_status"] = PlatformStatus(check, "quest");
            report["errors"] = ((List<object>)check["findings"]).Cast<Dictionary<string, object>>()
                .Where(f => (string)f["severity"] == "error").Select(f => (object)((string)f["code"] + (f.ContainsKey("platform") ? " [" + f["platform"] + "]" : ""))).ToList();
        }

        static string PlatformStatus(Dictionary<string, object> report, string platform) =>
            (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])[platform])["status"];

        static void Done(Dictionary<string, object> report, string output, string message, int code)
        {
            report["message"] = message;
            AvatarPreflight.WriteReport(report, output);
            Debug.Log("Frosty Neon: " + message);
            EditorApplication.Exit(code);
        }

        static void Fail(Dictionary<string, object> report, string output, Exception error)
        {
            report["error"] = error.GetType().Name + ": " + error.Message;
            Debug.LogException(error);
            Done(report, output, "Could not finish: " + error.Message, 1);
        }

        static bool Has(string flag) => Environment.GetCommandLineArgs().Contains(flag);

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
