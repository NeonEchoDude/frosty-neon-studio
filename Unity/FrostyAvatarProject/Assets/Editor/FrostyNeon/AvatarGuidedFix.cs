// Guided Fix for avatars that came ready-made from their creator (stage 2, docs/STAGE2_PLAN.md, milestone 2).
// It repairs only what the check reports, and only through Unity's import settings: the creator's prefab, models,
// materials and textures are not edited. Every change is written to Logs/FrostyNeon/guided_fix_<avatar>.json with
// the value it had before, so Undo can put it back. Tested in AvatarGuidedFixSelfTest.
//
// What it fixes (all three stop a VRChat upload; the SDK's own checks are mirrored in AvatarPreflight):
//   mesh_read_write_disabled        model import setting Read/Write          -> on
//   sdk_legacy_blend_shape_normals  model import setting Legacy Blendshape Normals -> on
//   sdk_texture_streaming_mipmaps   texture import setting Streaming Mip Maps -> on

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
    public static class AvatarGuidedFix
    {
        public const string ReadWrite = "Read/Write";
        public const string LegacyNormals = "Legacy Blendshape Normals";
        public const string StreamingMips = "Streaming Mip Maps";

        /// <summary>Finding codes this fix repairs on any avatar, including a creator's ready-made prefab.</summary>
        public static readonly HashSet<string> Fixes = new HashSet<string>
        {
            "mesh_read_write_disabled", "sdk_legacy_blend_shape_normals", "sdk_texture_streaming_mipmaps",
        };

        [Serializable]
        public class Change
        {
            public string code;
            public string asset;
            public string setting;
            public bool before;
            public bool after;
            public string note;      // why the change could not be made (empty when it was made)
            public bool Made => string.IsNullOrEmpty(note);
            public override string ToString() => setting + " on " + Path.GetFileName(asset) + ": " + (before ? "on" : "off") + " -> " + (after ? "on" : "off");
        }

        [Serializable]
        public class Log
        {
            public string prefab;
            public string applied_at;
            public string status_before;
            public string status_after;
            public List<Change> changes = new List<Change>();
        }

        public static string LogPath(string prefabPath) => UndoRecord.PathFor("guided_fix", prefabPath);

        /// <summary>The import-setting changes the avatar needs; empty when there is nothing this fix can do.</summary>
        public static List<Change> Plan(string prefabPath)
        {
            var changes = new List<Change>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return changes;
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                IEnumerable<string> models = renderers.Select(SharedMesh).Where(m => m != null && AssetDatabase.Contains(m))
                                                      .Select(AssetDatabase.GetAssetPath).Distinct();
                foreach (string model in models)
                {
                    if (!(AssetImporter.GetAtPath(model) is ModelImporter importer)) continue;
                    if (!importer.isReadable)
                        changes.Add(new Change { code = "mesh_read_write_disabled", asset = model, setting = ReadWrite, before = false, after = true });
                    if (AvatarPreflight.NeedsLegacyBlendShapeNormals(importer))
                        changes.Add(new Change { code = "sdk_legacy_blend_shape_normals", asset = model, setting = LegacyNormals, before = false, after = true });
                }
                foreach (TextureImporter texture in AvatarPreflight.TexturesWithoutStreamingMipmaps(renderers))
                    changes.Add(new Change { code = "sdk_texture_streaming_mipmaps", asset = texture.assetPath, setting = StreamingMips, before = false, after = true });
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return changes;
        }

        /// <summary>Makes the planned changes, records them for Undo, and checks the avatar again.</summary>
        public static Log Apply(string prefabPath)
        {
            var log = new Log
            {
                prefab = prefabPath,
                applied_at = DateTime.UtcNow.ToString("o"),
                status_before = (string)AvatarPreflight.AnalyzeAsset(prefabPath, null)["status"],
            };
            List<Change> planned = Plan(prefabPath);
            // Earlier changes recorded for this avatar stay in the log, so Undo still restores them.
            Log earlier = ReadLog(prefabPath);
            if (earlier != null) log.changes.AddRange(earlier.changes.Where(c => c.Made));
            foreach (IGrouping<string, Change> group in planned.GroupBy(c => c.asset))
            {
                AssetImporter importer = AssetImporter.GetAtPath(group.Key);
                foreach (Change change in group)
                {
                    try { Set(importer, change.setting, change.after); }
                    catch (Exception error) { change.note = error.Message; }
                }
                importer.SaveAndReimport();
            }
            log.changes.AddRange(planned);
            log.status_after = (string)AvatarPreflight.AnalyzeAsset(prefabPath, null)["status"];
            WriteLog(log);
            return log;
        }

        /// <summary>Puts back every setting the fix changed on this avatar; returns how many. The log is removed.</summary>
        public static int Undo(string prefabPath)
        {
            Log log = ReadLog(prefabPath);
            if (log == null) return 0;
            int restored = 0;
            foreach (IGrouping<string, Change> group in log.changes.Where(c => c.Made).GroupBy(c => c.asset))
            {
                AssetImporter importer = AssetImporter.GetAtPath(group.Key);
                if (importer == null) continue;
                foreach (Change change in group)
                {
                    Set(importer, change.setting, change.before);
                    restored++;
                }
                importer.SaveAndReimport();
            }
            UndoRecord.Delete("guided_fix", prefabPath);
            return restored;
        }

        public static bool CanUndo(string prefabPath) => File.Exists(LogPath(prefabPath));

        public static Log ReadLog(string prefabPath)
        {
            string path = LogPath(prefabPath);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<Log>(File.ReadAllText(path)); }
            catch (ArgumentException) { return null; }
        }

        static void WriteLog(Log log) => UndoRecord.Write("guided_fix", log.prefab, JsonUtility.ToJson(log, true));

        static void Set(AssetImporter importer, string setting, bool value)
        {
            switch (setting)
            {
                case ReadWrite: ((ModelImporter)importer).isReadable = value; break;
                case LegacyNormals: AvatarPreflight.SetLegacyBlendShapeNormals((ModelImporter)importer, value); break;
                case StreamingMips: ((TextureImporter)importer).streamingMipmaps = value; break;
                default: throw new ArgumentException("Unknown setting " + setting);
            }
        }

        static Mesh SharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter ? filter.sharedMesh : null;
        }
    }

    /// <summary>
    /// Where an avatar's undo record (Guided Fix, Quest version) is kept in Logs/FrostyNeon. Records used to be named
    /// after the prefab's file name alone, so two avatars with the same file name in different folders shared one
    /// record and could undo each other's changes. The name now ends with a short code for the prefab's full path; a
    /// record under the old name is still used when it says it belongs to this prefab.
    /// </summary>
    public static class UndoRecord
    {
        const string Folder = "Logs/FrostyNeon";

        public static string PathFor(string kind, string prefab)
        {
            string current = Current(kind, prefab);
            if (File.Exists(current)) return current;
            string old = Old(kind, prefab);
            return File.Exists(old) && OwnerOf(old) == prefab ? old : current;
        }

        public static void Write(string kind, string prefab, string json)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Current(kind, prefab), json);
            string old = Old(kind, prefab);
            if (File.Exists(old) && OwnerOf(old) == prefab) File.Delete(old);
        }

        public static void Delete(string kind, string prefab)
        {
            for (string path = PathFor(kind, prefab); File.Exists(path); path = PathFor(kind, prefab)) File.Delete(path);
        }

        static string Current(string kind, string prefab) =>
            Folder + "/" + kind + "_" + Path.GetFileNameWithoutExtension(prefab) + "_" + PathCode(prefab) + ".json";

        static string Old(string kind, string prefab) => Folder + "/" + kind + "_" + Path.GetFileNameWithoutExtension(prefab) + ".json";

        /// <summary>A stable eight-letter code for a project path (FNV-1a), the same on every run and PC.</summary>
        public static string PathCode(string prefab)
        {
            uint hash = 2166136261;
            foreach (char c in prefab.Replace('\\', '/')) { hash ^= c; hash *= 16777619; }
            return hash.ToString("x8");
        }

        [Serializable]
        class Owner { public string prefab; }

        static string OwnerOf(string file)
        {
            try { return JsonUtility.FromJson<Owner>(File.ReadAllText(file))?.prefab; }
            catch (Exception) { return null; }
        }
    }
}
