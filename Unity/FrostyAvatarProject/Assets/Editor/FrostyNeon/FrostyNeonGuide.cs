// Frosty Neon > Avatar Guide (start here): the guided window for people who have an avatar but have never used
// Unity (stage 2, docs/STAGE2_PLAN.md), in the Frosty Neon look (FrostyTheme). First version, 2026-11-15:
// steps 1 (choose your avatar) and 2 (check it) are guided; a plain model is fixed and previewed in the Avatar Pipeline
// window, a ready-made avatar by the Guided Fix (AvatarGuidedFix: import settings only, with Undo), and it can get a
// separate Quest version (AvatarQuestVersion), which the Quest upload then uses. Step 3 also shows pictures of the
// avatar (or its Quest version) with the VRChat eye level marked, its height and floor notes (AvatarPreviewRender).
// VRoid .vrm files come in through UniVRM when the project has it (AvatarVroid). Uploading uses the
// same uploader as everywhere else (it asks before uploading); afterwards the window shows the result and what to look
// at in VRChat. The logic behind it is tested in AvatarGuideSelfTest and AvatarGuidedFixSelfTest; the window itself
// needs a person to look at it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public class FrostyNeonGuide : EditorWindow
    {
        const string AvatarKey = "FrostyNeon.Guide.Avatar";
        const string PendingPackageKey = "FrostyNeon.Guide.PendingPackage";

        string _avatar;                                   // the asset the person chose (avatar prefab or model)
        Dictionary<string, object> _report;
        string _checkedPath;
        List<string> _projectAvatars;
        List<string> _justImported;
        string _importMessage;
        bool _importing;
        Vector2 _scroll;
        bool _details;
        List<AvatarGuidedFix.Change> _fixPlan;            // what the Guided Fix would change (ready-made avatars)
        string _fixMessage;
        string _questVersion;                             // the avatar's Quest version, if made
        Dictionary<string, object> _questReport;          // the check of that Quest version
        string _questMessage;
        Texture2D[] _pictures;                            // front, three-quarter, back
        string _picturesOf;
        AvatarPreviewRender.Facts _facts;
        string _vroidMessage;
        const string PictureDir = "Temp/FrostyNeonGuide/previews";
        UploadResult _upload;
        DateTime _uploadReadAt;

        [Serializable]
        class UploadResult { public string prefab; public string platform; public bool uploaded; public string message; }

        [MenuItem("Frosty Neon/Avatar Guide (start here)", false, 0)]
        public static void Open()
        {
            var window = GetWindow<FrostyNeonGuide>("Avatar Guide");
            window.minSize = new Vector2(460, 560);
        }

        void OnEnable()
        {
            _avatar = EditorPrefs.GetString(AvatarKey, null);
            if (!string.IsNullOrEmpty(_avatar) && AssetDatabase.LoadAssetAtPath<GameObject>(_avatar) == null) _avatar = null;
            // A package with scripts makes Unity reload during the import; pick up where it left off.
            string pending = EditorPrefs.GetString(PendingPackageKey, null);
            if (!string.IsNullOrEmpty(pending))
            {
                EditorPrefs.DeleteKey(PendingPackageKey);
                if (File.Exists(pending)) EditorApplication.delayCall += () => Imported(AvatarPackage.FindAvatarPrefabs(AvatarPackage.ListPaths(pending)), null);
            }
        }

        void OnDisable() => ClearPictures();

        void ClearPictures()
        {
            if (_pictures != null) foreach (Texture2D picture in _pictures) if (picture) DestroyImmediate(picture);
            _pictures = null;
            _picturesOf = null;
        }

        void OnGUI()
        {
            FrostyTheme.Background(this);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            FrostyTheme.Header("FROSTY NEON  ·  AVATAR GUIDE", "From download to VRChat in four steps. Let's get you dressed up!");
            GUILayout.Space(6);

            string prefab = AvatarPrefabFor(_avatar);
            StepChoose();
            if (!string.IsNullOrEmpty(_avatar))
            {
                StepCheck();
                StepFix(prefab);
                StepUpload(prefab);
            }
            GUILayout.Space(8);
            GUILayout.Label("  Uploads are always private, and nothing goes up until you say so.", FrostyTheme.Muted);
            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- step 1
        void StepChoose()
        {
            FrostyTheme.BeginStep(1, "Pick your avatar", !string.IsNullOrEmpty(_avatar));
            GUILayout.Label("Got an avatar from Booth or another creator? It usually comes as a <b>.unitypackage</b>. Bring it in:", FrostyTheme.Body);
            using (new EditorGUI.DisabledScope(_importing))
                if (GUILayout.Button(_importing ? "Importing... hang tight" : "»  Import an avatar package", FrostyTheme.Button)) ImportPackage();
            if (_importMessage != null) GUILayout.Label(_importMessage, FrostyTheme.Body);
            GUILayout.Space(2);
            GUILayout.Label("Made your avatar in VRoid Studio? Export it as a <b>.vrm</b> file, then:", FrostyTheme.Muted);
            if (GUILayout.Button("»  Bring in a VRoid file (.vrm)", FrostyTheme.ButtonAlt)) BringInVroid();
            if (_vroidMessage != null) GUILayout.Label(_vroidMessage, FrostyTheme.Body);

            GUILayout.Space(6);
            GUILayout.Label("Already in this project? Pick one:", FrostyTheme.Muted);
            if (_projectAvatars == null) _projectAvatars = AvatarPackage.FindAvatarPrefabs().Where(p => !AvatarQuestVersion.IsQuestVersion(p)).ToList();
            if (_projectAvatars.Count == 0) GUILayout.Label("(none yet)", FrostyTheme.Muted);
            foreach (string path in _projectAvatars.Take(10))
            {
                bool on = path == _avatar;
                if (GUILayout.Button((on ? "»  " : "    ") + Path.GetFileNameWithoutExtension(path), on ? FrostyTheme.ChoiceOn : FrostyTheme.Choice) && !on)
                    Choose(path);
            }
            if (_projectAvatars.Count > 10) GUILayout.Label("... and " + (_projectAvatars.Count - 10) + " more (use the box below).", FrostyTheme.Muted);

            GUILayout.Space(4);
            GUILayout.Label("Or drag a model (.fbx) or avatar prefab into this box:", FrostyTheme.Muted);
            var current = string.IsNullOrEmpty(_avatar) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(_avatar);
            var picked = (GameObject)EditorGUILayout.ObjectField(GUIContent.none, current, typeof(GameObject), false);
            if (picked != current && picked != null) Choose(AssetDatabase.GetAssetPath(picked));
            if (GUILayout.Button("refresh list", FrostyTheme.Muted, GUILayout.ExpandWidth(false))) _projectAvatars = null;
            FrostyTheme.EndStep();
        }

        // ---------------------------------------------------------------- step 2
        void StepCheck()
        {
            bool done = _report != null && ((string)_report["status"] != "fail" ||
                        (PlatformStatus(_report, "pc") != "fail" && _questReport != null && PlatformStatus(_questReport, "quest") != "fail"));
            FrostyTheme.BeginStep(2, "Check it", done);
            GUILayout.Label("Chosen: <b>" + Path.GetFileNameWithoutExtension(_avatar) + "</b>", FrostyTheme.Body);
            if (GUILayout.Button(_report == null ? "»  Check my avatar" : "»  Check again", FrostyTheme.ButtonAlt)) Check();
            if (_report != null) DrawReport();
            FrostyTheme.EndStep();
        }

        void DrawReport()
        {
            string status = (string)_report["status"];
            string pcStatus = PlatformStatus(_report, "pc");
            // With a Quest version (step 3), Quest readiness is that version's.
            string questStatus = _questReport != null ? PlatformStatus(_questReport, "quest") : PlatformStatus(_report, "quest");
            GUILayout.Space(4);
            GUILayout.Label(status == "pass" ? "Looking fresh! Nothing to fix."
                          : status == "warn" ? "Good to go, with a few notes below."
                          : pcStatus != "fail" && questStatus != "fail" ? "Ready for PC and Quest!"
                          : pcStatus != "fail" ? "The PC version is ready! Quest needs a bit more (below)."
                          : "Almost there! Let's sort out the bits below first.", FrostyTheme.CardTitle);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (string platform in new[] { "pc", "quest" })
                {
                    string s = platform == "pc" ? pcStatus : questStatus;
                    FrostyTheme.Pill((platform == "pc" ? "PC" : _questReport != null ? "QUEST VERSION" : "QUEST") + "  " + (s == "fail" ? "NEEDS WORK" : "READY"), FrostyTheme.StatusColor(s));
                }
                GUILayout.FlexibleSpace();
            }
            foreach (var group in FindingText.Sort(_report, IsModel(_avatar)))
            {
                if (group.Value.Count == 0) continue;
                Color colour = group.Key == FindingText.MustFix ? FrostyTheme.Hot : group.Key == FindingText.FixedForYou ? FrostyTheme.Cyan : FrostyTheme.Amber;
                GUILayout.Space(6);
                FrostyTheme.Pill(group.Key.ToUpperInvariant() + "  (" + group.Value.Count + ")", colour);
                foreach (Dictionary<string, object> finding in group.Value)
                {
                    FindingText.Text text = FindingText.For(finding);
                    string platform = finding.TryGetValue("platform", out object p) ? (((string)p) == "quest" ? "   [Quest only]" : "   [PC only]") : "";
                    GUILayout.Label("•  <b>" + text.Title + "</b>" + platform, FrostyTheme.Body);
                    GUILayout.Label("    " + text.Meaning, FrostyTheme.Muted);
                    if (finding.TryGetValue("objects", out object objects))
                    {
                        var list = (List<object>)objects;
                        GUILayout.Label("    " + string.Join(", ", list.Take(6)) + (list.Count > 6 ? ", ..." : ""), FrostyTheme.Muted);
                    }
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button((_details ? "hide" : "show") + " technical details", FrostyTheme.Muted, GUILayout.ExpandWidth(false))) _details = !_details;
            if (_details)
            {
                GUILayout.Label("Checked: " + _checkedPath, FrostyTheme.Muted);
                foreach (Dictionary<string, object> finding in ((List<object>)_report["findings"]).Cast<Dictionary<string, object>>())
                    GUILayout.Label(finding["severity"] + "  " + finding["code"] + ": " + finding["message"], FrostyTheme.Muted);
            }
        }

        // ---------------------------------------------------------------- step 3
        void StepFix(string prefab)
        {
            bool model = IsModel(_avatar);
            FrostyTheme.BeginStep(3, "Fix and preview", prefab != null && (_report == null || (string)_report["status"] != "fail"));
            if (model)
            {
                GUILayout.Label("This is a plain model. The Avatar Pipeline fixes what it can, builds the avatar and takes " +
                                "photos of it: choose this model there and press <b>Prepare</b>. Then come back and check again.", FrostyTheme.Body);
                if (GUILayout.Button("»  Open the Avatar Pipeline", FrostyTheme.ButtonAlt)) EditorApplication.ExecuteMenuItem("Frosty Neon/Avatar Pipeline");
            }
            else
            {
                if (_report == null)
                    GUILayout.Label("Check the avatar first (step 2); this step then shows what can be fixed.", FrostyTheme.Body);
                else if (_fixPlan != null && _fixPlan.Count > 0)
                {
                    GUILayout.Label(_fixPlan.Count + " setting(s) need changing before VRChat accepts this avatar. Only Unity's import " +
                                    "settings change: the avatar, model and materials stay exactly as the creator made them, and you can undo it.", FrostyTheme.Body);
                    foreach (AvatarGuidedFix.Change change in _fixPlan.Take(6)) GUILayout.Label("    •  " + change, FrostyTheme.Muted);
                    if (_fixPlan.Count > 6) GUILayout.Label("    ... and " + (_fixPlan.Count - 6) + " more", FrostyTheme.Muted);
                    if (GUILayout.Button("»  Fix it for me", FrostyTheme.Button)) FixReadyMade(prefab);
                }
                else
                {
                    bool mustFix = FindingText.Sort(_report, false).First(g => g.Key == FindingText.MustFix).Value.Count > 0;
                    GUILayout.Label(mustFix ? "Nothing here the toolkit can change by itself. What's under <b>Must fix</b> needs you; each item says what to do."
                                  : NeedsQuestVersion() ? "The settings are all fine." : "Nothing to fix. Nice!", FrostyTheme.Body);
                }
                if (_fixMessage != null) GUILayout.Label(_fixMessage, FrostyTheme.Body);
                if (AvatarGuidedFix.CanUndo(prefab) && GUILayout.Button("undo the Fix", FrostyTheme.Muted, GUILayout.ExpandWidth(false))) UndoFix(prefab);
                QuestSection(prefab);
                if (ModelFor(_avatar) != null)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("Frosty Neon built this avatar from its model (<b>" + Path.GetFileName(ModelFor(_avatar)) + "</b>). " +
                                    "To rebuild it, open the model in the Avatar Pipeline and press <b>Prepare</b>.", FrostyTheme.Muted);
                    if (GUILayout.Button("»  Open the Avatar Pipeline", FrostyTheme.ButtonAlt)) EditorApplication.ExecuteMenuItem("Frosty Neon/Avatar Pipeline");
                }
            }
            if (prefab != null) Preview(prefab);
            FrostyTheme.EndStep();
        }

        void Preview(string prefab)
        {
            GUILayout.Space(8);
            FrostyTheme.Pill("PREVIEW", FrostyTheme.Pink);
            GUILayout.Label("Pictures of your avatar standing on the floor. The <color=#3EF2FF>cyan</color> mark is where your eyes will be in VRChat.", FrostyTheme.Muted);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_picturesOf == prefab ? "»  Take them again" : "»  Show me my avatar", FrostyTheme.ButtonAlt)) TakePictures(prefab);
                if (_questVersion != null && GUILayout.Button(_picturesOf == _questVersion ? "»  Quest look again" : "»  Show the Quest look", FrostyTheme.ButtonAlt))
                    TakePictures(_questVersion);
            }
            if (_pictures == null) return;
            GUILayout.Label("<b>" + Path.GetFileNameWithoutExtension(_picturesOf) + "</b>:  " + _facts.Height.ToString("0.00") + " m tall" +
                            (_facts.HasView ? ",  eyes at " + _facts.ViewHeight.ToString("0.00") + " m" : ""), FrostyTheme.Body);
            float size = Mathf.Min(170f, (position.width - 70f) / 3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (Texture2D picture in _pictures)
                    GUILayout.Label(picture, GUILayout.Width(size), GUILayout.Height(size));
                GUILayout.FlexibleSpace();
            }
            List<string> notes = AvatarPreviewRender.Notes(_facts);
            if (notes.Count == 0) GUILayout.Label("Feet on the floor and eyes in the head. Looking good!", FrostyTheme.Muted);
            foreach (string note in notes) GUILayout.Label("<color=#FFC94A>•  " + note + "</color>", FrostyTheme.Body);
        }

        void TakePictures(string prefab)
        {
            ClearPictures();
            try
            {
                if (Directory.Exists(PictureDir)) Directory.Delete(PictureDir, true);
                _facts = AvatarPreviewRender.Measure(prefab);
                _pictures = AvatarPreviewRender.Render(prefab, PictureDir, 512, true).Select(file =>
                {
                    var picture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    picture.LoadImage(File.ReadAllBytes(file));
                    return picture;
                }).ToArray();
                _picturesOf = prefab;
            }
            catch (Exception error)
            {
                ClearPictures();
                EditorUtility.DisplayDialog("Avatar Guide", "The pictures could not be taken: " + error.Message, "OK");
            }
        }

        bool NeedsQuestVersion() =>
            _report != null && PlatformStatus(_report, "quest") == "fail" &&
            ((List<object>)_report["findings"]).Cast<Dictionary<string, object>>().Any(f => (string)f["code"] == "sdk_quest_unsupported_shader");

        void QuestSection(string prefab)
        {
            if (_report == null || (_questVersion == null && !NeedsQuestVersion())) return;
            GUILayout.Space(8);
            FrostyTheme.Pill("QUEST VERSION", FrostyTheme.Violet);
            if (_questVersion == null)
            {
                GUILayout.Label("Quest can't show this avatar's materials. Frosty Neon can make a separate <b>Quest version</b>: a copy that uses " +
                                "VRChat's Quest toon shader with the same colours and textures, and smaller textures for Quest only. The PC avatar " +
                                "stays exactly as it is, and both go to the same avatar in VRChat.", FrostyTheme.Body);
                GUILayout.Label("Quest versions look simpler: no outlines or special effects, and cut-out edges become solid.", FrostyTheme.Muted);
                if (GUILayout.Button("»  Make a Quest version", FrostyTheme.ButtonAlt)) MakeQuest(prefab);
            }
            else
            {
                string status = _questReport == null ? null : PlatformStatus(_questReport, "quest");
                GUILayout.Label("Made: <b>" + Path.GetFileNameWithoutExtension(_questVersion) + "</b>. " +
                                (status == "fail" ? "It still has problems for Quest:" : "It passes the Quest check."), FrostyTheme.Body);
                if (_questReport != null)
                    foreach (Dictionary<string, object> finding in ((List<object>)_questReport["findings"]).Cast<Dictionary<string, object>>())
                    {
                        string code = (string)finding["code"];
                        bool forQuest = !finding.TryGetValue("platform", out object platform) || (string)platform == "quest";
                        if (forQuest && ((string)finding["severity"] == "error" || code == "performance_below_target"))
                            GUILayout.Label("    •  " + FindingText.For(finding).Title + ": " + FindingText.For(finding).Meaning, FrostyTheme.Muted);
                    }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("rebuild it", FrostyTheme.Muted, GUILayout.ExpandWidth(false))) MakeQuest(prefab);
                    if (GUILayout.Button("remove it", FrostyTheme.Muted, GUILayout.ExpandWidth(false))) RemoveQuest(prefab);
                    GUILayout.FlexibleSpace();
                }
            }
            if (_questMessage != null) GUILayout.Label(_questMessage, FrostyTheme.Body);
        }

        // ---------------------------------------------------------------- step 4
        void StepUpload(string prefab)
        {
            UploadResult last = LastUpload(prefab);
            FrostyTheme.BeginStep(4, "Upload to VRChat", last != null && last.uploaded);
            if (prefab == null)
            {
                GUILayout.Label("Nothing to upload yet: prepare the model first (step 3).", FrostyTheme.Body);
                FrostyTheme.EndStep();
                return;
            }
            GUILayout.Label("Goes to the VRChat account signed in to the VRChat SDK window, as a <b>private</b> avatar. " +
                            "You'll be asked to confirm first.", FrostyTheme.Body);
            AvatarVroid.VrmInfo terms = AvatarVroid.TermsFor(prefab);
            if (terms != null && terms.OnlyAuthorMayUse)
                GUILayout.Label("<color=#FFC94A>Its creator's terms (\"" + terms.Title + "\" by " + terms.Author + ") say only the author may use it as an " +
                                "avatar. Upload it only if that is you or you have their permission; you'll be asked to confirm.</color>", FrostyTheme.Body);
            bool questNow = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            GUILayout.Label("Unity is set up for <b>" + (questNow ? "Quest" : "PC") + "</b> uploads right now. Uploading for " +
                            (questNow ? "PC" : "Quest") + " switches it first (a minute or two); then press the button again.", FrostyTheme.Muted);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (bool quest in new[] { false, true })
                {
                    Dictionary<string, object> report = quest && _questReport != null ? _questReport : _report;
                    string status = report == null ? null : PlatformStatus(report, quest ? "quest" : "pc");
                    using (new EditorGUI.DisabledScope(status == "fail"))
                        if (GUILayout.Button(status == "fail" ? (quest ? "Quest: see step 3" : "PC: fix step 2 first")
                                                              : "»  Upload for " + (quest ? "Quest" : "PC"), quest ? FrostyTheme.ButtonAlt : FrostyTheme.Button))
                            AvatarUpload.Upload(prefab, NameFor(prefab), quest);
                }
            }
            if (_report == null) GUILayout.Label("Tip: check the avatar first (step 2) to see which versions are ready.", FrostyTheme.Muted);
            else if (_questVersion != null)
                GUILayout.Label("Quest uploads use the Quest version from step 3; both go to the same avatar in VRChat.", FrostyTheme.Muted);
            else if (PlatformStatus(_report, "quest") == "fail" && PlatformStatus(_report, "pc") != "fail")
                GUILayout.Label("The PC version is ready. " + (NeedsQuestVersion() ? "For Quest, make a Quest version in step 3." : "Quest needs more work (see step 2)."), FrostyTheme.Muted);

            if (last != null && last.uploaded) AfterUpload(last);
            else if (last != null)
                GUILayout.Label("Last try (" + last.platform + "): " + last.message, FrostyTheme.Muted);
            else
                GUILayout.Label("Then in VRChat: open your menu, go to <b>Avatars</b>, find it under <b>Uploaded</b> and put it on. Have fun out there!", FrostyTheme.Muted);
            FrostyTheme.EndStep();
        }

        void AfterUpload(UploadResult last)
        {
            GUILayout.Space(4);
            FrostyTheme.Pill(last.platform.ToUpperInvariant() + " VERSION UPLOADED", FrostyTheme.Mint);
            GUILayout.Label("Now try it on in VRChat:", FrostyTheme.CardTitle);
            GUILayout.Label("1.  Open your menu, go to <b>Avatars > Uploaded</b> and pick it (it can take a minute to show up).", FrostyTheme.Body);
            GUILayout.Label("2.  Stand still: feet on the floor, not sinking in or floating.", FrostyTheme.Body);
            GUILayout.Label("3.  Find a mirror: your view sits at the avatar's eyes, and the mouth moves when you talk.", FrostyTheme.Body);
            GUILayout.Label("4.  Wave: the hands and arms follow yours.", FrostyTheme.Body);
            GUILayout.Label("Something off? VRChat's log says why. Close VRChat, then open the newest output_log file from here:", FrostyTheme.Muted);
            if (GUILayout.Button("open the VRChat log folder", FrostyTheme.Muted, GUILayout.ExpandWidth(false)))
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat");
                string newest = Directory.Exists(folder) ? Directory.GetFiles(folder, "output_log_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (newest != null) EditorUtility.RevealInFinder(newest);
                else EditorUtility.DisplayDialog("Avatar Guide", "No VRChat log was found in " + folder + ". Start VRChat once, then try again.", "OK");
            }
        }

        /// <summary>The uploader's latest result if it is about this avatar; re-read only when the file changes.</summary>
        UploadResult LastUpload(string prefab)
        {
            if (prefab == null || !File.Exists(AvatarUpload.ResultPath)) return null;
            DateTime written = File.GetLastWriteTimeUtc(AvatarUpload.ResultPath);
            if (written != _uploadReadAt)
            {
                _uploadReadAt = written;
                try { _upload = JsonUtility.FromJson<UploadResult>(File.ReadAllText(AvatarUpload.ResultPath)); }
                catch (ArgumentException) { _upload = null; }
            }
            return _upload != null && (_upload.prefab == prefab || (_questVersion != null && _upload.prefab == _questVersion)) &&
                   !string.IsNullOrEmpty(_upload.platform) ? _upload : null;
        }

        static string PlatformStatus(Dictionary<string, object> report, string platform) =>
            (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])[platform])["status"];

        // ---------------------------------------------------------------- actions
        void BringInVroid()
        {
            string uniVrm = AvatarVroid.UniVrm();
            if (uniVrm == null)
            {
                if (EditorUtility.DisplayDialog("VRoid files need UniVRM", AvatarVroid.WhatToDo, "Open the UniVRM page", "OK"))
                    Application.OpenURL(AvatarVroid.UniVrmReleases);
                _vroidMessage = "Waiting for UniVRM: download it, import it above, then press this button again.";
                return;
            }
            string file = EditorUtility.OpenFilePanel("Choose the VRoid file", "", "vrm");
            if (string.IsNullOrEmpty(file)) return;
            // The file's own header first: VRM version and the creator's terms.
            AvatarVroid.VrmInfo info;
            try { info = AvatarVroid.ReadInfo(file); }
            catch (Exception error) { _vroidMessage = "That file could not be read as a VRM: " + error.Message; return; }
            if (!info.Readable) { _vroidMessage = AvatarVroid.Vrm1Message; return; }
            List<string> warnings = info.Warnings();
            string terms = string.Join("\n", info.Lines()) + (warnings.Count > 0 ? "\n\nHeads up:\n" + string.Join("\n", warnings) : "");
            if (!EditorUtility.DisplayDialog(warnings.Count > 0 ? "Check the creator's terms" : "Bring in this VRoid avatar?", terms,
                    info.ModificationProhibited ? "I'm the author / I have permission" : "Bring it in", "Cancel")) return;
            try
            {
                string copied = AvatarVroid.CopyIntoProject(file);
                _vroidMessage = "Reading " + Path.GetFileName(copied) + " with " + uniVrm + "... hang tight.";
                // UniVRM builds the model on a later Editor update; wait for it (up to two minutes), then set it up.
                double deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.CallbackFunction wait = null;
                wait = () =>
                {
                    string made = AvatarVroid.PrefabFor(copied);
                    if (made == null && EditorApplication.timeSinceStartup < deadline) return;
                    EditorApplication.update -= wait;
                    _vroidMessage = made == null
                        ? "UniVRM did not make a model from " + Path.GetFileName(copied) + ". It may be a VRM 1.0 file; in VRoid Studio, export as VRM 0.0 and try again."
                        : null;
                    if (made != null) Choose(made);
                    _projectAvatars = null;
                    Repaint();
                };
                EditorApplication.update += wait;
            }
            catch (Exception error) { _vroidMessage = "That didn't work: " + error.Message; }
        }

        void Choose(string path)
        {
            if (AvatarQuestVersion.IsQuestVersion(path)) path = AvatarQuestVersion.PcPrefabFor(path) ?? path;
            // A model UniVRM made from a VRoid file becomes a VRChat avatar (Name_Avatar.prefab beside it).
            if (AvatarVroid.IsVrmPrefab(path))
            {
                try
                {
                    path = AvatarVroid.SetUpForVrchat(path, out string lipSync);
                    _vroidMessage = "Your VRoid avatar is set up for VRChat as " + Path.GetFileName(path) + ": eyes placed, " + lipSync + ". Now check it.";
                }
                catch (Exception error) { _vroidMessage = "Could not set up the VRoid model: " + error.Message; }
            }
            _avatar = path;
            _report = null;
            _fixPlan = null;
            _fixMessage = null;
            _questVersion = null;
            _questReport = null;
            _questMessage = null;
            ClearPictures();
            EditorPrefs.SetString(AvatarKey, path);
            GUI.FocusControl(null);
        }

        void ImportPackage()
        {
            string file = EditorUtility.OpenFilePanel("Choose the avatar package", "", "unitypackage");
            if (string.IsNullOrEmpty(file)) return;
            Dictionary<string, object> info;
            try { info = AvatarPackage.Inspect(file); }
            catch (Exception error)
            {
                EditorUtility.DisplayDialog("Avatar Guide", "This file could not be read as a Unity package:\n" + error.Message, "OK");
                return;
            }
            var scripts = (List<object>)info["scripts"];
            string summary = "This package has " + info["file_count"] + " files: " + ((List<object>)info["prefabs"]).Count + " prefab(s), " +
                             ((List<object>)info["models"]).Count + " model(s), " + ((List<object>)info["shaders"]).Count + " shader file(s).";
            if (scripts.Count > 0)
                summary += "\n\nHeads up: it also contains " + scripts.Count + " script file(s). Unity runs scripts as soon as they are imported, " +
                           "so only import this if you trust where it came from (for example the creator's own Booth page):\n" +
                           string.Join("\n", scripts.Take(8).Select(s => "  " + s)) + (scripts.Count > 8 ? "\n  ..." : "");
            if (!EditorUtility.DisplayDialog("Import this avatar package?", summary, "Import", "Cancel")) return;
            _importing = true;
            _importMessage = null;
            EditorPrefs.SetString(PendingPackageKey, file);    // in case the import reloads Unity
            AvatarPackage.ImportAsync(file, (avatars, error) =>
            {
                EditorPrefs.DeleteKey(PendingPackageKey);
                Imported(avatars, error);
            });
        }

        void Imported(List<string> avatars, string error)
        {
            _importing = false;
            _justImported = avatars;
            _projectAvatars = null;
            _importMessage = error != null ? "That didn't work: " + error
                           : avatars.Count == 0 ? "Imported! But there's no ready avatar inside. If it only has a model, drag the model (.fbx) into the box below."
                           : "Imported! Found " + avatars.Count + " avatar(s); the first one is picked for you.";
            if (avatars.Count > 0) Choose(avatars[0]);
            Repaint();
        }

        void Check()
        {
            _checkedPath = AvatarPrefabFor(_avatar) ?? _avatar;
            _report = AvatarPreflight.AnalyzeAsset(_checkedPath, null);
            _fixPlan = IsModel(_avatar) ? null : AvatarGuidedFix.Plan(_checkedPath);
            _questVersion = IsModel(_avatar) ? null : AvatarQuestVersion.QuestPrefabFor(_checkedPath);
            _questReport = _questVersion == null ? null : AvatarPreflight.AnalyzeAsset(_questVersion, null);
            _details = false;
        }

        void MakeQuest(string prefab)
        {
            if (_questVersion == null && !EditorUtility.DisplayDialog("Make a Quest version?",
                    "Frosty Neon will add a FrostyNeon_Quest folder next to this avatar with a Quest copy and Quest materials, and make " +
                    "the textures those materials use smaller for Quest only.\n\nThe avatar itself, its materials and its PC look are not " +
                    "changed. You can remove the Quest version again from the Guide.", "Make it", "Cancel")) return;
            try
            {
                AvatarQuestVersion.Result made = AvatarQuestVersion.Make(prefab);
                Check();
                _questMessage = "Done! Made " + made.Converted.Count + " Quest material(s)" +
                                (made.Kept.Count > 0 ? ", kept " + made.Kept.Count + " that Quest already accepts" : "") + "." +
                                (made.Textures.Count > 0 ? " " + made.Textures.Count + " texture(s) are now at most " + AvatarQuestVersion.AndroidMaxSize + " pixels on Quest." : "") +
                                (made.SeeThrough.Count > 0 ? " " + made.SeeThrough.Count + " see-through part(s) (like eyelashes or highlights) use Quest's see-through shaders." : "") +
                                (made.CutOut.Count > 0 ? " Cut-out edges become solid on Quest: " + string.Join(", ", made.CutOut.Take(6)) + (made.CutOut.Count > 6 ? ", ..." : "") + "." : "") +
                                (made.IdNote != null ? " " + made.IdNote : "");
            }
            catch (Exception error) { _questMessage = "That didn't work: " + error.Message; }
        }

        void RemoveQuest(string prefab)
        {
            if (!EditorUtility.DisplayDialog("Remove the Quest version?", "Delete the Quest copy and its materials, and put back the " +
                    "texture settings it changed? An avatar already uploaded to VRChat is not affected.", "Remove", "Cancel")) return;
            int restored = AvatarQuestVersion.Remove(prefab);
            Check();
            _questMessage = "Removed the Quest version and put back " + restored + " texture setting(s).";
        }

        void FixReadyMade(string prefab)
        {
            if (!EditorUtility.DisplayDialog("Fix this avatar?",
                    "These Unity import settings will change:\n" + string.Join("\n", _fixPlan.Select(c => "  " + c)) +
                    "\n\nThe avatar's own files are not edited, and you can undo this from the Guide.", "Fix it", "Cancel")) return;
            int planned = _fixPlan.Count;
            AvatarGuidedFix.Log log = AvatarGuidedFix.Apply(prefab);
            var failed = log.changes.Where(c => !c.Made).ToList();
            Check();
            _fixMessage = failed.Count == 0
                ? "Done! Changed " + planned + " setting(s) and checked again: " + (PlatformStatus(_report, "pc") == "fail" ? "see step 2 for what is left." : "the PC version is ready.")
                : "Changed " + (planned - failed.Count) + " of " + planned + " setting(s). Could not change: " +
                  string.Join("; ", failed.Select(c => c + " (" + c.note + ")"));
        }

        void UndoFix(string prefab)
        {
            if (!EditorUtility.DisplayDialog("Undo the Fix?", "Put back the import settings the Fix changed on this avatar?", "Undo", "Cancel")) return;
            int restored = AvatarGuidedFix.Undo(prefab);
            Check();
            _fixMessage = "Put back " + restored + " setting(s).";
        }

        public static bool IsModel(string path) =>
            !string.IsNullOrEmpty(path) && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        /// <summary>The avatar prefab to check and upload: the chosen prefab, or the _Avatar prefab Prepare made from a model.</summary>
        public static string AvatarPrefabFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!IsModel(path)) return path;
            string prefab = path.Substring(0, path.LastIndexOf('.')) + "_Avatar.prefab";
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefab) != null ? prefab : null;
        }

        /// <summary>For an avatar prefab Frosty Neon made (X_Avatar.prefab beside X.fbx), the model it was made from; otherwise null.</summary>
        public static string ModelFor(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || !prefab.EndsWith("_Avatar.prefab", StringComparison.Ordinal)) return null;
            string model = prefab.Substring(0, prefab.Length - "_Avatar.prefab".Length) + ".fbx";
            return AssetDatabase.LoadAssetAtPath<GameObject>(model) != null ? model : null;
        }

        static string NameFor(string prefab)
        {
            string name = Path.GetFileNameWithoutExtension(prefab);
            return name.EndsWith("_Avatar", StringComparison.Ordinal) ? name.Substring(0, name.Length - "_Avatar".Length) : name;
        }
    }
}
