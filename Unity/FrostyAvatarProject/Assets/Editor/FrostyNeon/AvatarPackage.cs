// Frosty Neon avatar package import (Editor-only). Stage 2, step 1.1 (docs/STAGE2_PLAN.md).
//
// Most avatars bought on Booth arrive as a .unitypackage: a ready avatar prefab with its model, materials and
// textures. This tool:
//   1. Reads the package WITHOUT importing it and lists what it contains (Inspect). Scripts are listed separately:
//      Unity compiles and runs scripts from a package as soon as it is imported, so they deserve a look first.
//      A package that cannot be listed completely, or that names a file outside Assets/ and Packages/, is refused.
//   2. Imports it (Import), then finds the avatar prefab(s) it brought in: prefabs with a VRChat avatar descriptor.
//   3. Reports whether the imported avatar's materials need a shader that is not installed (the preflight's
//      material_shader_missing finding).
// Nothing in the package is changed. Importing adds the package's files to the project at the paths it names;
// files that already exist are replaced by Unity, as with Assets > Import Package.
//
// Batch entry (Unity closed), used by `frosty.ps1 import`:
//   -executeMethod FrostyNeon.Editor.AvatarPackage.Run -packageFile <file.unitypackage> [-packageInspectOnly]
//   [-packageAllowScripts]
// Writes Logs/FrostyNeon/package_import.json. A package with scripts is only imported with -packageAllowScripts.
// Exit code 0: inspected (and imported, unless inspect-only or refused); 1: unreadable, refused or failed.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class AvatarPackage
    {
        const string ReportPath = "Logs/FrostyNeon/package_import.json";
        static readonly string[] ScriptExtensions = { ".cs", ".dll", ".js", ".asmdef", ".asmref", ".rsp" };

        /// <summary>
        /// The asset paths a .unitypackage would create, read from the archive without importing it. Throws
        /// InvalidDataException, so nothing is imported, when the list could be incomplete (a cut-short archive, tar
        /// features Unity's exporter does not write) or a path points outside Assets/ and Packages/.
        /// </summary>
        public static List<string> ListPaths(string packageFile)
        {
            var paths = new List<string>();
            bool ended = false;
            using (var file = File.OpenRead(packageFile))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFully(gzip, header, 512))
                {
                    if (header.All(b => b == 0)) { ended = true; break; }        // end of archive
                    string name = Ascii(header, 0, 100);
                    string prefix = Ascii(header, 345, 155);
                    if (prefix.Length > 0) name = prefix + "/" + name;
                    string sizeText = Ascii(header, 124, 12).Trim();
                    long size = sizeText.Length == 0 ? 0 : Convert.ToInt64(sizeText, 8);
                    char type = (char)header[156];
                    long padded = (size + 511) / 512 * 512;
                    // Long-name and extended headers can rename the next entry, so a list that skipped them could miss
                    // a script; links can point outside the folder being unpacked. Unity's exporter writes none of them.
                    if (type == 'L' || type == 'K' || type == 'x' || type == 'g' || type == '1' || type == '2')
                        throw new InvalidDataException(Unchecked + " (it uses tar entries that Unity's own packages never contain).");
                    if ((type == '0' || type == '\0' || type == '7') && name.EndsWith("/pathname"))
                    {
                        if (size > MaxPathnameBytes) throw new InvalidDataException(Unchecked + " (an entry's path is " + size + " bytes long).");
                        var data = new byte[padded];
                        if (!ReadFully(gzip, data, (int)padded)) break;
                        string path = Encoding.UTF8.GetString(data, 0, (int)size).Split('\n')[0].Trim().Replace('\\', '/');
                        if (path.Length == 0) continue;
                        if (!IsSafePath(path)) throw new InvalidDataException("The package wants to write to \"" + path + "\", which is not a safe place inside the project's Assets or Packages folder. Nothing was imported.");
                        paths.Add(path);
                    }
                    else Skip(gzip, padded);
                }
            }
            if (!ended) throw new InvalidDataException(Unchecked + " (the file ends early: it may be cut short by a download, or not a Unity package).");
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        const string Unchecked = "This package could not be fully read, so it could not be checked for scripts. Nothing was imported";
        const int MaxPathnameBytes = 64 * 1024;

        /// <summary>
        /// True for a package path Unity keeps inside the project: under Assets/ or Packages/, no "..", no drive or
        /// stream colon, no control characters, and no segment Windows would rename by dropping trailing dots or spaces.
        /// </summary>
        public static bool IsSafePath(string path)
        {
            if (!(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))) return false;
            if (path.IndexOf(':') >= 0 || path.Any(char.IsControl)) return false;
            foreach (string segment in path.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".") || segment.EndsWith(" ")) return false;
            return true;
        }

        /// <summary>What a package contains, in groups a person can judge: scripts, avatar prefabs, models, shaders.</summary>
        public static Dictionary<string, object> Inspect(string packageFile)
        {
            List<string> paths = ListPaths(packageFile);
            List<string> Of(params string[] extensions) =>
                paths.Where(p => extensions.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();
            return new Dictionary<string, object>
            {
                ["package"] = packageFile,
                ["file_count"] = paths.Count,
                ["scripts"] = Of(ScriptExtensions).Cast<object>().ToList(),
                ["prefabs"] = Of(".prefab").Cast<object>().ToList(),
                ["models"] = Of(".fbx", ".obj", ".blend", ".vrm").Cast<object>().ToList(),
                ["shaders"] = Of(".shader", ".cginc", ".hlsl", ".orlshader").Cast<object>().ToList(),
                ["folders"] = paths.Select(p => string.Join("/", p.Split('/').Take(2))).Distinct().Cast<object>().ToList(),
            };
        }

        /// <summary>Prefabs (under the given paths, or the whole project) whose root has a VRChat avatar descriptor.</summary>
        public static List<string> FindAvatarPrefabs(IEnumerable<string> prefabPaths = null)
        {
            IEnumerable<string> candidates = prefabPaths ??
                AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath);
            return candidates.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                             .Where(p =>
                             {
                                 var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                                 return prefab != null && prefab.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() != null;
                             })
                             .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Imports the package without a dialog. Unity imports packages in the background, so the result arrives in
        /// <paramref name="done"/>: the avatar prefabs the package contains, or an error message. Call Inspect first
        /// and show the scripts to the person; this method does not ask. If the package has scripts, Unity reloads
        /// during the import and this callback is lost; callers that must survive that keep their own marker (see
        /// PendingPath and the guide window).
        /// </summary>
        public static void ImportAsync(string packageFile, Action<List<string>, string> done, double timeoutSeconds = 600)
        {
            List<string> listed = ListPaths(packageFile);
            string name = Path.GetFileNameWithoutExtension(packageFile);
            double started = EditorApplication.timeSinceStartup;
            bool finished = false;
            AssetDatabase.ImportPackageCallback completed = null;
            AssetDatabase.ImportPackageFailedCallback failed = null;
            AssetDatabase.ImportPackageCallback cancelled = null;
            EditorApplication.CallbackFunction watchdog = null;
            void Finish(string error)
            {
                if (finished) return;
                finished = true;
                AssetDatabase.importPackageCompleted -= completed;
                AssetDatabase.importPackageFailed -= failed;
                AssetDatabase.importPackageCancelled -= cancelled;
                EditorApplication.update -= watchdog;
                List<string> avatars = new List<string>();
                if (error == null)
                {
                    try
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        avatars = FindAvatarPrefabs(listed);
                    }
                    catch (Exception findError) { error = "The package was imported, but its avatars could not be listed: " + findError.Message; }
                }
                done(avatars, error);
            }
            completed = package => { if (package == name) Finish(null); };
            failed = (package, message) => { if (package == name) Finish("Unity could not import the package: " + message); };
            cancelled = package => { if (package == name) Finish("The import was cancelled."); };
            // Unity does not always send the completed event for a non-interactive import (measured 2026-11-15 in
            // batch mode: the files arrived, the event did not), so also finish once every listed file is in the
            // project.
            watchdog = () =>
            {
                if (AllImported(listed)) Finish(null);
                else if (EditorApplication.timeSinceStartup - started > timeoutSeconds)
                    Finish("The import did not finish within " + timeoutSeconds + " seconds.");
            };
            AssetDatabase.importPackageCompleted += completed;
            AssetDatabase.importPackageFailed += failed;
            AssetDatabase.importPackageCancelled += cancelled;
            EditorApplication.update += watchdog;
            AssetDatabase.ImportPackage(packageFile, false);
            if (!finished && AllImported(listed)) Finish(null);      // the import may already be done
        }

        static bool AllImported(List<string> listed) =>
            listed.Count > 0 && listed.All(p => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p, AssetPathToGUIDOptions.OnlyExistingAssets)));

        /// <summary>Preflight summary for each avatar prefab: status per platform and the missing-shader finding.</summary>
        public static List<object> CheckAvatars(IEnumerable<string> prefabs)
        {
            var results = new List<object>();
            foreach (string prefab in prefabs)
            {
                Dictionary<string, object> report = AvatarPreflight.AnalyzeAsset(prefab, null);
                var platforms = (Dictionary<string, object>)report["platforms"];
                var findings = ((List<object>)report["findings"]).Cast<Dictionary<string, object>>().ToList();
                results.Add(new Dictionary<string, object>
                {
                    ["prefab"] = prefab,
                    ["status"] = report["status"],
                    ["pc_status"] = ((Dictionary<string, object>)platforms["pc"])["status"],
                    ["quest_status"] = ((Dictionary<string, object>)platforms["quest"])["status"],
                    ["findings"] = findings.Select(f => (object)f["code"]).ToList(),
                    ["missing_shaders"] = findings.Where(f => (string)f["code"] == "material_shader_missing")
                                                  .SelectMany(f => f.TryGetValue("objects", out object o) ? (List<object>)o : new List<object>())
                                                  .ToList(),
                });
            }
            return results;
        }

        /// <summary>Marker for a batch import in progress, so it can be finished after Unity reloads for the package's scripts.</summary>
        public const string PendingPath = "Temp/frosty_package_import_pending.txt";

        public static void Run()
        {
            var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-unity-package-import" };
            try
            {
                string file = Argument("-packageFile");
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) throw new FileNotFoundException("No package file given or found: " + file);
                Dictionary<string, object> inspected = Inspect(file);
                report["inspected"] = inspected;
                var scripts = (List<object>)inspected["scripts"];
                bool allowScripts = Environment.GetCommandLineArgs().Contains("-packageAllowScripts");
                if (Environment.GetCommandLineArgs().Contains("-packageInspectOnly"))
                {
                    report["mode"] = "inspect_only";
                    Done(report, "Inspected only; nothing was imported.", 0);
                }
                else if (scripts.Count > 0 && !allowScripts)
                {
                    report["mode"] = "refused";
                    Done(report, "The package contains " + scripts.Count + " script file(s), which Unity would run when they are " +
                                 "imported. Nothing was imported. If you trust where the package came from, import it again " +
                                 "with -AllowScripts (frosty.ps1 import), or from the Avatar Guide, which asks first.", 1);
                }
                else
                {
                    Directory.CreateDirectory("Temp");
                    File.WriteAllText(PendingPath, file);
                    ImportAsync(file, (avatars, error) => Imported(file, report, avatars, error));
                }
            }
            catch (Exception error)
            {
                report["error"] = error.GetType().Name + ": " + error.Message;
                Debug.LogException(error);
                Done(report, "The package could not be read or imported: " + error.Message, 1);
            }
        }

        // After a reload (a package with scripts), finish a batch import that was in progress.
        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (!Application.isBatchMode || !File.Exists(PendingPath)) return;
            // Only the import command itself resumes. A marker left behind by a crashed import must not take over (and
            // end) some other batch command such as a self-test.
            string file = File.ReadAllText(PendingPath).Trim();
            if (Argument("-executeMethod") != typeof(AvatarPackage).FullName + ".Run" || Argument("-packageFile") != file)
            {
                if (Argument("-executeMethod") != typeof(AvatarPackage).FullName + ".Run") File.Delete(PendingPath);
                return;
            }
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(PendingPath)) return;
                var report = new Dictionary<string, object> { ["tool"] = "frosty-neon-unity-package-import", ["resumed_after_reload"] = true };
                try { report["inspected"] = Inspect(file); } catch (Exception) { }
                Imported(file, report, FindAvatarPrefabs(ListPaths(file)), null);
            };
        }

        static void Imported(string file, Dictionary<string, object> report, List<string> avatars, string error)
        {
            if (File.Exists(PendingPath)) File.Delete(PendingPath);
            if (error != null)
            {
                report["mode"] = "failed";
                Done(report, error, 1);
                return;
            }
            report["mode"] = "imported";
            report["avatar_prefabs"] = avatars.Cast<object>().ToList();
            try { report["avatars"] = CheckAvatars(avatars); }
            catch (Exception checkError)
            {
                // This runs from an Editor update: an exception here would leave batch-mode Unity running forever.
                Debug.LogException(checkError);
                report["error"] = checkError.GetType().Name + ": " + checkError.Message;
                Done(report, "Imported, but the avatar check could not finish: " + checkError.Message + ". Check the avatar in the Avatar Guide.", 1);
                return;
            }
            Done(report, avatars.Count == 0
                ? "Imported, but no ready avatar prefab (with a VRChat avatar descriptor) was found in the package. If it only contains a model, check that model (an .fbx) instead."
                : "Imported. Avatar prefab(s): " + string.Join(", ", avatars), 0);
        }

        static void Done(Dictionary<string, object> report, string message, int code)
        {
            report["message"] = message;
            AvatarPreflight.WriteReport(report, ReportPath);
            Debug.Log("Frosty Neon package import: " + message);
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static string Ascii(byte[] data, int start, int length)
        {
            int end = start;
            while (end < start + length && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, start, end - start);
        }

        static bool ReadFully(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        static void Skip(Stream stream, long count)
        {
            var buffer = new byte[81920];
            while (count > 0)
            {
                int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (n <= 0) return;
                count -= n;
            }
        }
    }
}
