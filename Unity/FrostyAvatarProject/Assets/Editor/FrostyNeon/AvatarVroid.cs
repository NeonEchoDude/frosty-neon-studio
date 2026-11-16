// VRoid Studio files (.vrm) for the Avatar Guide (stage 2, docs/STAGE2_PLAN.md, step 1.3). Unity cannot read a .vrm
// file by itself; it needs UniVRM, the VRM project's own Unity add-on. Adding UniVRM to a project is the person's
// choice (and the owner's for this repository), so this only detects it, explains how to add it, and, once it is
// there, copies the .vrm file into the project so UniVRM turns it into a model the toolkit can check like any other.
//
// UniVRM (0.x) turns Assets/.../Name.vrm into Name.prefab beside it, on the next Editor update. That prefab is a
// humanoid with VRM data but no VRChat descriptor; SetUpForVrchat() makes Name_Avatar.prefab from it with
// AvatarDescriptorSetup (view position at the eyes, lip sync when the 15 visemes exist). A VRoid face has only the five
// vowel shapes (Fcl_MTH_A ...), so when the visemes are missing, lip sync is wired as the SDK's "Jaw Flap Blend Shape"
// with Fcl_MTH_A as the mouth-open shape. This file does not reference UniVRM's code, so it compiles without UniVRM.
//
// ReadInfo() reads a .vrm file's own header before anything is imported: its VRM version (0.x or 1.0; the UniVRM in
// this project reads 0.x) and the creator's terms that every VRM file carries (who may use it as an avatar, whether
// it may be modified, credit). The Guide and frosty.ps1 vroid show these. Terms that prohibit changes stop the set-up
// (it changes the avatar) unless the person confirms; terms that allow only the author to use it as an avatar are
// checked at upload (TermsFor + AvatarUpload), because looking at and checking an avatar locally is not wearing it.
// Found 2026-11-15 with VRoid's AvatarSample_C (VRM 1.0, only the author, no changes) and VRoid Studio's sample P
// exported by the owner (VRM 0.0, only the author).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace FrostyNeon.Editor
{
    public static class AvatarVroid
    {
        public const string UniVrmReleases = "https://github.com/vrm-c/UniVRM/releases";
        public const string ImportFolder = "Assets/FrostyNeon/VRoid";

        // A type each UniVRM generation has: VRM 0.x (VRMMeta) and VRM 1.0 (Vrm10Instance).
        static readonly (string type, string label)[] Markers = { ("VRM.VRMMeta", "UniVRM (VRM 0.x)"), ("UniVRM10.Vrm10Instance", "UniVRM (VRM 1.0)") };

        /// <summary>Which UniVRM is in the project ("UniVRM (VRM 0.x)", "UniVRM (VRM 1.0)", both joined), or null when none is.</summary>
        public static string UniVrm()
        {
            var found = Markers.Where(m => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(m.type, false) != null)).Select(m => m.label).ToList();
            return found.Count == 0 ? null : string.Join(" and ", found);
        }

        public static string WhatToDo =>
            "Unity needs a free add-on called UniVRM to open VRoid (.vrm) files, and this project does not have it yet.\n\n" +
            "1. Download the UniVRM .unitypackage for Unity 2022 from the VRM project's page:\n   " + UniVrmReleases + "\n" +
            "2. Bring it in with \"Import an avatar package\" in the Avatar Guide. It contains scripts from the VRM project; " +
            "the Guide lists them before importing.\n3. Then choose \"Bring in a VRoid file\" again.";

        /// <summary>Copies a .vrm file into Assets/FrostyNeon/VRoid (never over an existing file) and imports it. Returns its project path.</summary>
        public static string CopyIntoProject(string file)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("File not found: " + file);
            if (!file.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Not a .vrm file: " + file);
            if (!AssetDatabase.IsValidFolder(ImportFolder))
            {
                // A folder made only on disk is unknown to Unity until imported, and GenerateUniqueAssetPath needs it.
                Directory.CreateDirectory(ImportFolder);
                AssetDatabase.ImportAsset(ImportFolder, ImportAssetOptions.ForceSynchronousImport);
            }
            string target = AssetDatabase.GenerateUniqueAssetPath(ImportFolder + "/" + Path.GetFileName(file));
            if (string.IsNullOrEmpty(target)) throw new IOException("Unity could not choose a file name in " + ImportFolder + " for " + Path.GetFileName(file) + ".");
            File.Copy(file, target);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            return target;
        }

        public const string VroidMouthOpen = "Fcl_MTH_A";

        /// <summary>The prefab UniVRM made from this .vrm file (Name.prefab beside it), or null while it is not there yet.</summary>
        public static string PrefabFor(string vrm)
        {
            string prefab = vrm.Substring(0, vrm.Length - ".vrm".Length) + ".prefab";
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefab) != null ? prefab : null;
        }

        /// <summary>True for a prefab UniVRM made from a .vrm file beside it.</summary>
        public static bool IsVrmPrefab(string path) =>
            !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path.Substring(0, path.Length - ".prefab".Length) + ".vrm");

        /// <summary>
        /// Makes Name_Avatar.prefab (a VRChat avatar) from the prefab UniVRM made; leaves an existing one as it is.
        /// Returns the avatar prefab's path and a short note on lip sync.
        /// </summary>
        public static string SetUpForVrchat(string vrmPrefab, out string lipSyncNote)
        {
            string avatar = AvatarDescriptorSetup.DefaultPrefabPath(vrmPrefab);
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(avatar) != null;
            AvatarDescriptorSetup.Execute(vrmPrefab, avatar, true);
            lipSyncNote = existed ? "already set up earlier; left as it was" : null;
            if (existed) return avatar;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(avatar))
                lipSyncNote = WireJawFlap(scope.prefabContentsRoot);
            return avatar;
        }

        /// <summary>
        /// When lip sync is not wired yet and a face mesh has VRoid's Fcl_MTH_A shape, sets the SDK's Jaw Flap Blend Shape
        /// lip sync with it. Returns what was done, in plain words.
        /// </summary>
        public static string WireJawFlap(GameObject root)
        {
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return "no avatar descriptor";
            if (descriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape) return "mouth moves with speech (all 15 visemes)";
            foreach (SkinnedMeshRenderer face in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = face.sharedMesh;
                if (mesh == null || mesh.GetBlendShapeIndex(VroidMouthOpen) < 0) continue;
                descriptor.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape;
                descriptor.VisemeSkinnedMesh = face;
                descriptor.MouthOpenBlendShapeName = VroidMouthOpen;
                return "mouth opens with speech (VRoid's " + VroidMouthOpen + " shape on " + face.name + ")";
            }
            return "no mouth shapes found, so the mouth will not move";
        }

        public class VrmInfo
        {
            public string Version;            // "0.x" or "1.0"
            public string Title;
            public string Author;
            public string AvatarUse;          // who may use it as an avatar, as written in the file
            public string Modification;       // 1.0 only; null in 0.x files (0.x has no such field)
            public string Commercial;
            public string Credit;             // 1.0 only
            public string Licence;            // licence name or URL
            public bool OnlyAuthorMayUse;     // the terms do not allow anyone but the author to wear it
            public bool ModificationProhibited;
            public bool Readable => Version == "0.x";   // by the UniVRM (VRM 0.x) in this project

            /// <summary>The creator's terms in plain words, one line each.</summary>
            public List<string> Lines()
            {
                var lines = new List<string> { "\"" + Title + "\" by " + Author + " (VRM " + Version + ")" };
                lines.Add("Who may use it as an avatar: " + AvatarUse);
                if (Modification != null) lines.Add("Changes allowed: " + Modification);
                if (Commercial != null) lines.Add("Commercial use: " + Commercial);
                if (Credit != null) lines.Add("Credit: " + Credit);
                if (Licence != null) lines.Add("Licence: " + Licence);
                return lines;
            }

            /// <summary>Warnings about the terms, empty when they allow wearing and changing it.</summary>
            public List<string> Warnings()
            {
                var warnings = new List<string>();
                if (OnlyAuthorMayUse)
                    warnings.Add("Its terms say only its author may use it as an avatar. You can bring it in and check it, but only upload it to VRChat if you are the author or have their permission.");
                if (ModificationProhibited)
                    warnings.Add("Its terms prohibit changes. Setting it up for VRChat, the Fix and a Quest version all change it.");
                return warnings;
            }
        }

        /// <summary>Reads the VRM version and the creator's terms from a .vrm (glTF binary) file without importing it.</summary>
        public static VrmInfo ReadInfo(string file)
        {
            byte[] head;
            using (var stream = File.OpenRead(file))
            {
                var header = new byte[20];
                if (stream.Read(header, 0, 20) != 20 || Encoding.ASCII.GetString(header, 0, 4) != "glTF")
                    throw new InvalidDataException("Not a VRM file (no glTF header): " + file);
                int jsonLength = BitConverter.ToInt32(header, 12);
                if (jsonLength <= 0 || jsonLength > 64 * 1024 * 1024) throw new InvalidDataException("Unreadable VRM header: " + file);
                head = new byte[jsonLength];
                if (stream.Read(head, 0, jsonLength) != jsonLength) throw new InvalidDataException("The VRM file is cut short: " + file);
            }
            JObject json = JObject.Parse(Encoding.UTF8.GetString(head));
            var info = new VrmInfo();
            JObject vrm1 = json["extensions"]?["VRMC_vrm"]?["meta"] as JObject;
            JObject vrm0 = json["extensions"]?["VRM"]?["meta"] as JObject;
            if (vrm1 != null)
            {
                info.Version = "1.0";
                info.Title = (string)vrm1["name"];
                info.Author = string.Join(", ", (vrm1["authors"] as JArray)?.Select(a => (string)a) ?? new string[0]);
                string permission = (string)vrm1["avatarPermission"];
                info.OnlyAuthorMayUse = permission == "onlyAuthor";
                info.AvatarUse = permission == "onlyAuthor" ? "only the author" : permission == "onlySeparatelyLicensedPerson" ? "only people with a separate licence from the author" : permission == "everyone" ? "everyone" : permission;
                string modification = (string)vrm1["modification"];
                info.ModificationProhibited = modification == "prohibited";
                info.Modification = modification == "prohibited" ? "no" : modification == "allowModification" ? "yes" : modification == "allowModificationRedistribution" ? "yes, and sharing changed versions" : modification;
                string commercial = (string)vrm1["commercialUsage"];
                info.Commercial = commercial == "personalNonProfit" ? "personal, non-profit only" : commercial == "personalProfit" ? "personal, including for profit" :
                                  commercial == "corporation" ? "allowed, including companies" : commercial;
                info.Credit = (string)vrm1["creditNotation"] == "required" ? "required" : "not required";
                info.Licence = (string)vrm1["licenseUrl"];
            }
            else if (vrm0 != null)
            {
                info.Version = "0.x";
                info.Title = (string)vrm0["title"];
                info.Author = (string)vrm0["author"];
                string allowed = (string)vrm0["allowedUserName"];
                info.OnlyAuthorMayUse = allowed == "OnlyAuthor";
                info.AvatarUse = allowed == "OnlyAuthor" ? "only the author" : allowed == "ExplicitlyLicensedPerson" ? "only people the author has licensed" : allowed == "Everyone" ? "everyone" : allowed;
                string commercial = (string)vrm0["commercialUssageName"];
                info.Commercial = commercial == "Disallow" ? "not allowed" : commercial == "Allow" ? "allowed" : commercial;
                string licence = (string)vrm0["licenseName"];
                info.Licence = licence == "Other" ? (string)vrm0["otherLicenseUrl"] : licence;
                // VRM 0.x has no separate "modification" field; the CC licences that say NoDerivatives forbid changes.
                info.ModificationProhibited = licence != null && licence.Contains("_ND");
            }
            else throw new InvalidDataException("This glTF file has no VRM data: " + file);
            if (string.IsNullOrEmpty(info.Title)) info.Title = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(info.Author)) info.Author = "(no author given)";
            return info;
        }

        /// <summary>
        /// For an avatar made from a .vrm file (Name_Avatar.prefab, or its Quest version, beside Name.vrm), that file's terms;
        /// null for any other avatar or when the file cannot be read.
        /// </summary>
        public static VrmInfo TermsFor(string avatarPrefab)
        {
            if (string.IsNullOrEmpty(avatarPrefab)) return null;
            string path = avatarPrefab.Replace('\\', '/');
            if (AvatarQuestVersion.IsQuestVersion(path))
                path = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/') + "/" +
                       Path.GetFileName(path).Substring(0, Path.GetFileName(path).Length - "_Quest.prefab".Length) + ".prefab";
            if (!path.EndsWith("_Avatar.prefab", StringComparison.Ordinal)) return null;
            string vrm = path.Substring(0, path.Length - "_Avatar.prefab".Length) + ".vrm";
            if (!File.Exists(vrm)) return null;
            try { return ReadInfo(vrm); }
            catch (Exception) { return null; }
        }

        public const string Vrm1Message =
            "This is a VRM 1.0 file. The UniVRM in this project reads VRM 0.0 files. If you made the avatar in VRoid Studio, " +
            "export it again and choose VRM 0.0 in the export settings.";

        /// <summary>.vrm files already in the project.</summary>
        public static List<string> VrmFilesInProject() =>
            AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                                                        p.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
    }
}
