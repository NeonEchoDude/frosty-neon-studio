// Self-test for stage 2 step 1 (docs/STAGE2_PLAN.md): package reading and import (AvatarPackage), the missing-shader
// check (AvatarPreflight), the plain-language texts (FindingText) and the guide window's helpers (FrostyNeonGuide).
// Batch mode, uploads nothing; everything it creates is removed. Run:
//   Unity.exe -batchmode -projectPath Unity\FrostyAvatarProject -executeMethod FrostyNeon.Editor.AvatarGuideSelfTest.Run -logFile <log>
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostyNeon.Editor
{
    public static class AvatarGuideSelfTest
    {
        const string Ready = "Assets/FrostyNeon/Fixtures/fbx_humanoid_fixture_v1_ready.fbx";
        const string Robot = "Assets/FrostyNeon/Avatars/FrostyNeonBot.fbx";
        const string TempFolder = "Assets/FrostyNeon/_GuideSelfTest";
        const string PackagePrefab = TempFolder + "/Packaged_Avatar.prefab";
        const string WorkDir = "Temp/FrostyNeonGuideSelfTest";
        const string OutPath = "Logs/FrostyNeon/avatar_guide_selftest.json";

        static readonly List<object> Results = new List<object>();
        static bool _failed;

        // Unity imports packages in the background, so the test runs in two parts: everything up to the import, then
        // (in the import's callback) the rest, the report and the exit.
        public static void Run()
        {
            try { Execute(); }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon guide self-test crashed: " + error);
                Finish(false);
            }
        }

        static void Finish(bool ok)
        {
            AssetDatabase.DeleteAsset(TempFolder);
            if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static void Execute()
        {
            Results.Clear();
            _failed = false;
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.CreateFolder("Assets/FrostyNeon", "_GuideSelfTest");
            Directory.CreateDirectory(WorkDir);

            // 1. Plain-language texts cover exactly the codes the preflight can produce.
            string source = File.ReadAllText("Assets/Editor/FrostyNeon/AvatarPreflight.cs");
            var codes = new HashSet<string>(Regex.Matches(source, "Add\\(findings, \"(?:error|warning)\", \"([a-z_]+)\"")
                                                 .Cast<Match>().Select(m => m.Groups[1].Value));
            var missing = codes.Where(c => !FindingText.Texts.ContainsKey(c)).ToList();
            var extra = FindingText.Texts.Keys.Where(c => !codes.Contains(c)).ToList();
            Check("every preflight finding code (" + codes.Count + ") has plain-language text, and no text is left over",
                  codes.Count >= 31 && missing.Count == 0 && extra.Count == 0,
                  "missing: " + string.Join(",", missing) + " | extra: " + string.Join(",", extra));
            Check("texts are filled in", FindingText.Texts.Values.All(t => t.Title.Length > 3 && t.Meaning.Length > 20), "");

            // 2. Grouping.
            var fixable = Finding("error", "mesh_read_write_disabled");
            var wrongShader = Finding("error", "sdk_quest_unsupported_shader");
            var skeleton = Finding("error", "humanoid_rest_pose_scale_mismatch");
            Check("import-setting and Quest-shader problems go under 'fixed for you' on models and prefabs; model-only fixes go under 'must fix' on a prefab",
                  FindingText.Group(fixable, true) == FindingText.FixedForYou && FindingText.Group(fixable, false) == FindingText.FixedForYou &&
                  FindingText.Group(skeleton, true) == FindingText.FixedForYou && FindingText.Group(skeleton, false) == FindingText.MustFix &&
                  FindingText.Group(wrongShader, false) == FindingText.FixedForYou, "");
            Check("errors that cannot be fixed go under 'must fix'; warnings under 'good to know'",
                  FindingText.Group(Finding("error", "sdk_feet_not_specified"), true) == FindingText.MustFix &&
                  FindingText.Group(Finding("warning", "non_unit_scale"), true) == FindingText.GoodToKnow, "");

            // 3. Missing shaders, with the package guessed from the material's saved properties.
            string lil = MissingShaderMaterial("lil_skin", "_lilToonVersion", "_lilShadowColor");
            string poi = MissingShaderMaterial("poi_skin", "_PoiParsedVersion", "shader_master_label");
            string unknown = MissingShaderMaterial("odd_skin", "_SomethingElse", "_Glossiness");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var lilMat = AssetDatabase.LoadAssetAtPath<Material>(lil);
            var poiMat = AssetDatabase.LoadAssetAtPath<Material>(poi);
            var oddMat = AssetDatabase.LoadAssetAtPath<Material>(unknown);
            Check("materials whose shader is not installed are recognised; the shader package is guessed (lilToon, Poiyomi, unknown)",
                  lilMat && AvatarPreflight.ShaderMissing(lilMat) && AvatarPreflight.GuessShaderPackage(lilMat) == "lilToon" &&
                  AvatarPreflight.GuessShaderPackage(poiMat) == "Poiyomi Toon Shader" && AvatarPreflight.GuessShaderPackage(oddMat) == null,
                  (lilMat ? lilMat.shader.name : "no material") + " | " + AvatarPreflight.GuessShaderPackage(lilMat) + " | " + AvatarPreflight.GuessShaderPackage(poiMat));
            var standard = new Material(Shader.Find("Standard"));
            Check("a material with an installed shader is not reported", !AvatarPreflight.ShaderMissing(standard) &&
                  AvatarPreflight.GuessShaderPackage(standard) == null, standard.shader.name);
            UnityEngine.Object.DestroyImmediate(standard);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Ready), scene);
                instance.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials = new[] { lilMat, poiMat };
                Dictionary<string, object> report = AvatarPreflight.Analyze(instance, null, Ready);
                var finding = ((List<object>)report["findings"]).Cast<Dictionary<string, object>>()
                              .FirstOrDefault(f => (string)f["code"] == "material_shader_missing");
                string message = finding != null ? (string)finding["message"] : "";
                Check("preflight reports material_shader_missing as an error naming both packages; PC status fail",
                      finding != null && (string)finding["severity"] == "error" && message.Contains("lilToon") &&
                      message.Contains("Poiyomi") && ((List<object>)finding["objects"]).Count == 2 &&
                      (string)((Dictionary<string, object>)((Dictionary<string, object>)report["platforms"])["pc"])["status"] == "fail",
                      message);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            // 3b. Preview facts and the eye-level mark (step 3), and VRoid detection (step 1).
            const string robotPrefab = "Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab";
            AvatarPreviewRender.Facts facts = AvatarPreviewRender.Measure(robotPrefab);
            Check("the robot measures as a standing avatar: a sensible height, feet on the floor, eyes in the head, no notes",
                  facts.Height > 0.5f && facts.Height < 3f && Mathf.Abs(facts.Lowest) < 0.03f && facts.HasView &&
                  facts.ViewHeight > facts.Height * 0.6f && facts.ViewHeight < facts.Height && AvatarPreviewRender.Notes(facts).Count == 0,
                  "height " + facts.Height + ", lowest " + facts.Lowest + ", eyes " + facts.ViewHeight);
            var sinking = new AvatarPreviewRender.Facts { Height = 1.6f, Lowest = -0.3f, ViewHeight = 0.2f, HasView = true };
            List<string> sinkingNotes = AvatarPreviewRender.Notes(sinking);
            Check("notes flag an avatar 30 cm in the floor and a view far from the head",
                  sinkingNotes.Count == 2 && sinkingNotes[0].Contains("30 cm below the floor") && sinkingNotes[1].Contains("not near the head"),
                  string.Join(" | ", sinkingNotes));
            int marked = CyanPixels(AvatarPreviewRender.Render(robotPrefab, WorkDir + "/marked", 256, true)[0]);
            int plain = CyanPixels(AvatarPreviewRender.Render(robotPrefab, WorkDir + "/plain", 256, false)[0]);
            Check("the eye-level mark is drawn into the front picture in the Frosty Neon cyan", marked > 100 && plain < 5,
                  "cyan pixels with mark " + marked + ", without " + plain);
            bool uniVrmFiles = Directory.Exists("Packages/com.vrmc.univrm") || Directory.Exists("Assets/VRM");
            Check("VRoid: whether UniVRM is in the project is detected correctly (the Guide explains how to add it when it is not)",
                  (AvatarVroid.UniVrm() != null) == uniVrmFiles && AvatarVroid.WhatToDo.Contains(AvatarVroid.UniVrmReleases),
                  "UniVRM files " + uniVrmFiles + ", detected " + (AvatarVroid.UniVrm() ?? "none"));
            string vrm1 = Path.Combine(WorkDir, "terms_1.vrm"), vrm0 = Path.Combine(WorkDir, "terms_0.vrm");
            WriteGlb(vrm1, "{\"asset\":{\"version\":\"2.0\"},\"extensions\":{\"VRMC_vrm\":{\"meta\":{\"name\":\"Sample\",\"authors\":[\"Someone\"]," +
                           "\"avatarPermission\":\"onlyAuthor\",\"modification\":\"prohibited\",\"commercialUsage\":\"personalNonProfit\"," +
                           "\"creditNotation\":\"required\",\"licenseUrl\":\"https://vrm.dev/licenses/1.0/\"}}}}");
            WriteGlb(vrm0, "{\"asset\":{\"version\":\"2.0\"},\"extensions\":{\"VRM\":{\"meta\":{\"title\":\"Open\",\"author\":\"Maker\"," +
                           "\"allowedUserName\":\"Everyone\",\"commercialUssageName\":\"Allow\",\"licenseName\":\"CC_BY\"}}}}");
            AvatarVroid.VrmInfo restricted = AvatarVroid.ReadInfo(vrm1), open = AvatarVroid.ReadInfo(vrm0);
            Check("VRM header: a VRM 1.0 file with 'only the author' and 'no changes' is read as not importable here, with both warnings",
                  restricted.Version == "1.0" && !restricted.Readable && restricted.OnlyAuthorMayUse && restricted.ModificationProhibited &&
                  restricted.Warnings().Count == 2 && restricted.Lines().Contains("Who may use it as an avatar: only the author") &&
                  restricted.Lines().Contains("Commercial use: personal, non-profit only"), string.Join(" | ", restricted.Lines()));
            Check("VRM header: a VRM 0.x file open to everyone (CC BY) is importable with no warnings",
                  open.Version == "0.x" && open.Readable && !open.OnlyAuthorMayUse && !open.ModificationProhibited && open.Warnings().Count == 0 &&
                  open.Author == "Maker" && open.Licence == "CC_BY", string.Join(" | ", open.Lines()));
            File.Copy(vrm1, Path.Combine(WorkDir, "Termed.vrm"));
            string termedAvatar = WorkDir + "/Termed_Avatar.prefab", termedQuest = WorkDir + "/FrostyNeon_Quest/Termed_Avatar_Quest.prefab";
            Check("an avatar made from a .vrm (and its Quest version) is traced back to the file's terms; other avatars are not",
                  AvatarVroid.TermsFor(termedAvatar)?.OnlyAuthorMayUse == true && AvatarVroid.TermsFor(termedQuest)?.Title == "Sample" &&
                  AvatarVroid.TermsFor("Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab") == null, "");
            var face = new GameObject("VroidTest");
            var faceMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            faceMesh.AddBlendShapeFrame("Fcl_MTH_A", 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            var faceRenderer = new GameObject("Face").AddComponent<SkinnedMeshRenderer>();
            faceRenderer.transform.SetParent(face.transform);
            faceRenderer.sharedMesh = faceMesh;
            var vroidDescriptor = face.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            string jaw = AvatarVroid.WireJawFlap(face);
            Check("VRoid lip sync: a face with only VRoid's Fcl_MTH_A shape gets the SDK's jaw-flap lip sync with that shape",
                  vroidDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape &&
                  vroidDescriptor.VisemeSkinnedMesh == faceRenderer && vroidDescriptor.MouthOpenBlendShapeName == "Fcl_MTH_A", jaw);
            UnityEngine.Object.DestroyImmediate(face);
            UnityEngine.Object.DestroyImmediate(faceMesh);

            // 4. Reading a package without importing it: a hand-built package with a script and a prefab.
            string handmade = Path.Combine(WorkDir, "handmade.unitypackage");
            WritePackage(handmade, new[] { "Assets/Shop/Avatar/Cute_Avatar.prefab", "Assets/Shop/Avatar/Cute.fbx",
                                           "Assets/Shop/Editor/Installer.cs", "Assets/Shop/Plugins/Tool.dll", "Assets/Shop/Shaders/Skin.shader" });
            Dictionary<string, object> info = AvatarPackage.Inspect(handmade);
            Check("package contents listed without importing: 5 files, 2 scripts, 1 prefab, 1 model, 1 shader",
                  (int)info["file_count"] == 5 && ((List<object>)info["scripts"]).Count == 2 && ((List<object>)info["prefabs"]).Count == 1 &&
                  ((List<object>)info["models"]).Count == 1 && ((List<object>)info["shaders"]).Count == 1 && !Directory.Exists("Assets/Shop"),
                  string.Join(", ", ((List<object>)info["scripts"]).Cast<string>()));
            var refused = new List<string>();
            foreach (string unsafePath in new[] { "Assets/../../Evil.cs", "C:/Windows/Evil.cs", "Assets/Evil.cs.", "Assets/Notes.txt:x.cs", "ProjectSettings/Evil.cs" })
            {
                string unsafeFile = Path.Combine(WorkDir, "unsafe.unitypackage");
                WritePackage(unsafeFile, new[] { "Assets/Shop/Avatar/Cute_Avatar.prefab", unsafePath });
                try { AvatarPackage.ListPaths(unsafeFile); }
                catch (InvalidDataException) { refused.Add(unsafePath); }
            }
            Check("a package that would write outside Assets/ or Packages/ (or to a name Windows renames) is refused before importing",
                  refused.Count == 5 && AvatarPackage.IsSafePath("Packages/com.example/Runtime/A.cs"), "refused: " + string.Join(", ", refused));

            // 5. A real package exported by Unity: an avatar prefab is found again after importing it.
            AvatarDescriptorSetup.Execute(Ready, PackagePrefab, true);
            string exported = Path.Combine(WorkDir, "exported.unitypackage");
            AssetDatabase.ExportPackage(PackagePrefab, exported, ExportPackageOptions.Default);
            List<string> listed = AvatarPackage.ListPaths(exported);
            AssetDatabase.DeleteAsset(PackagePrefab);
            bool goneBefore = AssetDatabase.LoadAssetAtPath<GameObject>(PackagePrefab) == null;
            AvatarPackage.ImportAsync(exported, (found, error) =>
            {
                bool ok = false;
                try { ok = AfterImport(listed, goneBefore, found, error); }
                catch (Exception crash) { Debug.LogError("Frosty Neon guide self-test crashed after the import: " + crash); }
                Finish(ok);
            }, 120);
        }

        static bool AfterImport(List<string> listed, bool goneBefore, List<string> found, string error)
        {
            Check("Unity-exported package: listed, imported, and its avatar prefab found again (with its descriptor)",
                  error == null && listed.Contains(PackagePrefab) && goneBefore && found.SequenceEqual(new[] { PackagePrefab }),
                  (error ?? "") + " listed " + listed.Count + ", found " + string.Join(",", found));
            List<object> checks = AvatarPackage.CheckAvatars(found);
            Check("the imported avatar is checked: PC pass, Quest fail (Standard shader), no missing shaders",
                  checks.Count == 1 && (string)((Dictionary<string, object>)checks[0])["pc_status"] == "pass" &&
                  (string)((Dictionary<string, object>)checks[0])["quest_status"] == "fail" &&
                  ((List<object>)((Dictionary<string, object>)checks[0])["missing_shaders"]).Count == 0, "");
            Check("avatar prefabs in the project include the starter avatar and the imported one",
                  AvatarPackage.FindAvatarPrefabs().Contains("Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab") &&
                  AvatarPackage.FindAvatarPrefabs().Contains(PackagePrefab), "");

            // 6. Window helpers.
            Check("guide: a model maps to its prepared _Avatar prefab, a prefab to itself, an unprepared model to none",
                  FrostyNeonGuide.AvatarPrefabFor(Robot) == "Assets/FrostyNeon/Avatars/FrostyNeonBot_Avatar.prefab" &&
                  FrostyNeonGuide.AvatarPrefabFor(PackagePrefab) == PackagePrefab && FrostyNeonGuide.AvatarPrefabFor(Ready) == null &&
                  FrostyNeonGuide.IsModel(Robot) && !FrostyNeonGuide.IsModel(PackagePrefab), "");

            AvatarPreflight.WriteReport(new Dictionary<string, object>
            {
                ["tool"] = "frosty-neon-unity-avatar-guide-selftest",
                ["unity_version"] = Application.unityVersion,
                ["status"] = _failed ? "fail" : "pass",
                ["checks"] = Results,
            }, OutPath);
            Debug.Log("Frosty Neon guide self-test: " + (_failed ? "FAILED" : "PASS") + ", " + Results.Count + " checks");
            return !_failed;
        }

        /// <summary>A minimal glTF binary (.vrm) holding only a JSON chunk.</summary>
        static void WriteGlb(string file, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            int padded = (body.Length + 3) / 4 * 4;
            using (var stream = File.Create(file))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("glTF"));
                writer.Write(2);
                writer.Write(12 + 8 + padded);
                writer.Write(padded);
                writer.Write(Encoding.ASCII.GetBytes("JSON"));
                writer.Write(body);
                for (int i = body.Length; i < padded; i++) writer.Write((byte)' ');
            }
        }

        static int CyanPixels(string png)
        {
            var image = new Texture2D(2, 2);
            image.LoadImage(File.ReadAllBytes(png));
            Color32 cyan = AvatarPreviewRender.MarkColor;
            int count = image.GetPixels32().Count(c => c.r == cyan.r && c.g == cyan.g && c.b == cyan.b);
            UnityEngine.Object.DestroyImmediate(image);
            return count;
        }

        static Dictionary<string, object> Finding(string severity, string code) =>
            new Dictionary<string, object> { ["severity"] = severity, ["code"] = code, ["message"] = "" };

        /// <summary>Writes a material file that points at a shader that does not exist, with the given saved properties.</summary>
        static string MissingShaderMaterial(string name, string floatName, string otherName)
        {
            string path = TempFolder + "/" + name + ".mat";
            File.WriteAllText(path,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n" +
                "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
                "  m_PrefabAsset: {fileID: 0}\n  m_Name: " + name + "\n" +
                "  m_Shader: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n" +
                "  m_ValidKeywords: []\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 0\n" +
                "  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: -1\n  stringTagMap: {}\n  disabledShaderPasses: []\n" +
                "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs: []\n    m_Ints: []\n" +
                "    m_Floats:\n    - " + floatName + ": 1\n    - " + otherName + ": 0\n    m_Colors: []\n  m_BuildTextureStacks: []\n");
            return path;
        }

        /// <summary>A minimal .unitypackage (gzip tar): one folder per file with 'pathname' and an empty 'asset'.</summary>
        static void WritePackage(string file, IEnumerable<string> paths)
        {
            using (var output = File.Create(file))
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                foreach (string path in paths)
                {
                    string guid = Guid.NewGuid().ToString("N");
                    TarEntry(gzip, guid + "/pathname", Encoding.UTF8.GetBytes(path + "\n00"));
                    TarEntry(gzip, guid + "/asset", new byte[0]);
                }
                gzip.Write(new byte[1024], 0, 1024);
            }
        }

        static void TarEntry(Stream stream, string name, byte[] data)
        {
            var header = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(header, 100);
            Encoding.ASCII.GetBytes(Convert.ToString(data.Length, 8).PadLeft(11, '0') + "\0").CopyTo(header, 124);
            Encoding.ASCII.GetBytes("00000000000\0").CopyTo(header, 136);
            header[156] = (byte)'0';
            Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(header, 257);
            for (int i = 148; i < 156; i++) header[i] = (byte)' ';
            int sum = header.Sum(b => b);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
            stream.Write(header, 0, 512);
            stream.Write(data, 0, data.Length);
            int pad = (512 - data.Length % 512) % 512;
            stream.Write(new byte[pad], 0, pad);
        }

        static void Check(string name, bool ok, string detail)
        {
            Results.Add(new Dictionary<string, object> { ["check"] = name, ["ok"] = ok, ["detail"] = detail });
            if (!ok) _failed = true;
            Debug.Log("  [" + (ok ? "ok" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
        }
    }
}
