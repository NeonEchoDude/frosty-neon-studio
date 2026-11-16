// Quest version of an avatar prefab (stage 2, docs/STAGE2_PLAN.md). Most bought avatars use PC shaders (lilToon,
// Poiyomi, Standard), which VRChat refuses on Quest: the installed SDK (3.10.6) only allows the shaders in
// VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList ("VRChat/Mobile/..."). Its component list is the same for PC
// and Quest, so shaders are what stops the Quest upload. Quest avatars also have hard size limits
// (VRC.ValidationHelpers: 10 MB download, 40 MB uncompressed).
//
// Make() builds, beside the creator's files and without changing them:
//   <prefab folder>/FrostyNeon_Quest/<name>_Quest.prefab   a prefab VARIANT of the avatar, so it follows the creator's
//                                                           prefab and shares its VRChat avatar ID (one avatar, two platforms)
//   <prefab folder>/FrostyNeon_Quest/Materials/*.mat        one "VRChat/Mobile/Toon Standard" material for each material
//                                                           Quest refuses, with its colour and main texture
// and caps the Android size of those main textures at 1024 (an Android-only import override; PC is not affected).
// Each override is recorded with its old values in Logs/FrostyNeon/quest_version_<name>.json; Remove() puts them back
// and deletes the folder. Colours and textures are read from the material's saved data, so this also works for
// materials whose shader is not installed (pink). Tested in AvatarQuestVersionSelfTest.
//
// Toon Standard is opaque. Materials that are truly transparent on PC (MToon blend mode Transparent, or a render queue
// above 2500: eyelashes, eyeline, brows, iris, eye highlights on a VRoid avatar) would draw as solid shapes over the
// face, so they get one of the two see-through shaders Quest allows instead: VRChat/Mobile/Particles/Multiply (where the
// texture is transparent nothing changes; dark or coloured parts tint what is behind: lines, brows, iris) or, for
// mostly bright textures (highlights), VRChat/Mobile/Particles/Additive. Both take only the texture, not a colour.
// Cut-out materials (MToon Cutout, queue 2450-2500) stay on Toon Standard; their cut edges become solid and are
// reported. Found 2026-11-15 on VRoid Studio's sample P: 6 transparent and 10 cut-out materials.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;

namespace FrostyNeon.Editor
{
    public static class AvatarQuestVersion
    {
        public const string QuestShader = "VRChat/Mobile/Toon Standard";
        public const string MultiplyShader = "VRChat/Mobile/Particles/Multiply";
        public const string AdditiveShader = "VRChat/Mobile/Particles/Additive";
        public const string FolderName = "FrostyNeon_Quest";
        public const int AndroidMaxSize = 1024;
        static readonly string[] TextureNames = { "_MainTex", "_BaseMap", "_BaseColorMap" };
        static readonly string[] ColorNames = { "_Color", "_BaseColor" };

        [Serializable]
        public class TextureChange
        {
            public string texture;
            public bool before_overridden;
            public int before_max_size;
            public int before_format;
            public override string ToString() => "Quest size of " + Path.GetFileName(texture) + ": max " + AndroidMaxSize;
        }

        [Serializable]
        public class Log
        {
            public string prefab;
            public string quest_prefab;
            public string made_at;
            public List<TextureChange> textures = new List<TextureChange>();
        }

        public class Result
        {
            public string QuestPrefab;
            public List<string> Converted = new List<string>();     // "material -> Quest material"
            public List<string> Kept = new List<string>();          // materials Quest already accepts
            public List<string> SeeThrough = new List<string>();    // transparent on PC: given a see-through Quest shader
            public List<string> CutOut = new List<string>();        // cut-out on PC: solid edges on Quest
            public List<TextureChange> Textures = new List<TextureChange>();
            public string IdNote;
        }

        public static string FolderFor(string prefab) => Path.GetDirectoryName(prefab).Replace('\\', '/') + "/" + FolderName;
        public static string QuestPathFor(string prefab) => FolderFor(prefab) + "/" + Path.GetFileNameWithoutExtension(prefab) + "_Quest.prefab";
        public static string LogPath(string prefab) => UndoRecord.PathFor("quest_version", prefab);

        public static bool IsQuestVersion(string path) =>
            !string.IsNullOrEmpty(path) && path.Replace('\\', '/').Contains("/" + FolderName + "/") && path.EndsWith("_Quest.prefab", StringComparison.Ordinal);

        /// <summary>The Quest version made from this avatar prefab, if there is one.</summary>
        public static string QuestPrefabFor(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || IsQuestVersion(prefab)) return null;
            string quest = QuestPathFor(prefab);
            return AssetDatabase.LoadAssetAtPath<GameObject>(quest) != null ? quest : null;
        }

        /// <summary>For a Quest version, the avatar prefab it was made from.</summary>
        public static string PcPrefabFor(string quest)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(quest);
            GameObject source = asset ? PrefabUtility.GetCorrespondingObjectFromSource(asset) : null;
            return source ? AssetDatabase.GetAssetPath(source) : null;
        }

        /// <summary>Materials on the avatar that Quest refuses (shader not in the SDK's list, or not installed).</summary>
        public static List<Material> RefusedMaterials(string prefab)
        {
            var refused = new List<Material>();
            ForEachRenderer(prefab, renderers =>
            {
                foreach (Material material in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                    if (!QuestAccepts(material)) refused.Add(material);
            });
            return refused;
        }

        public static bool QuestAccepts(Material material) =>
            material.shader != null && !AvatarPreflight.ShaderMissing(material) &&
            VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList.Contains(material.shader.name);

        /// <summary>Builds (or rebuilds) the Quest version. The creator's prefab, materials and textures are not edited.</summary>
        public static Result Make(string prefab)
        {
            var result = new Result { IdNote = SyncIds(prefab) };
            Shader shader = Shader.Find(QuestShader);
            if (shader == null) throw new InvalidOperationException("The shader " + QuestShader + " is not in this project (it comes with the VRChat SDK).");
            string folder = FolderFor(prefab);
            string materials = folder + "/Materials";
            string questPath = QuestPathFor(prefab);
            Log earlier = ReadLog(prefab);

            // Start clean: an earlier Quest version is replaced (its avatar ID was copied back by SyncIds above).
            AssetDatabase.DeleteAsset(questPath);
            AssetDatabase.DeleteAsset(materials);
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(prefab).Replace('\\', '/'), FolderName);
            AssetDatabase.CreateFolder(folder, "Materials");

            var replacements = new Dictionary<Material, Material>();
            var mainTextures = new HashSet<Texture>();
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), scene);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        Material original = slots[i];
                        if (original == null) continue;
                        if (QuestAccepts(original))
                        {
                            if (!result.Kept.Contains(original.name)) result.Kept.Add(original.name);
                            continue;
                        }
                        if (!replacements.TryGetValue(original, out Material quest))
                        {
                            SavedLook look = ReadSavedLook(original);
                            Shader questShader = shader;
                            if (look.Transparent) questShader = Shader.Find(MostlyBright(look.Texture) ? AdditiveShader : MultiplyShader) ?? shader;
                            quest = new Material(questShader) { name = original.name + "_Quest" };
                            if (look.Color.HasValue && quest.HasProperty("_Color")) quest.color = look.Color.Value;
                            if (look.Texture != null)
                            {
                                quest.mainTexture = look.Texture;
                                quest.mainTextureScale = look.Scale;
                                quest.mainTextureOffset = look.Offset;
                                mainTextures.Add(look.Texture);
                            }
                            AssetDatabase.CreateAsset(quest, AssetDatabase.GenerateUniqueAssetPath(materials + "/" + Safe(original.name) + "_Quest.mat"));
                            replacements[original] = quest;
                            result.Converted.Add(original.name + " -> " + quest.name + (questShader != shader ? " (" + questShader.name.Substring("VRChat/Mobile/Particles/".Length) + ")" : ""));
                            if (look.Transparent) result.SeeThrough.Add(original.name);
                            else if (look.CutOut) result.CutOut.Add(original.name);
                        }
                        slots[i] = quest;
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = slots;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, questPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            // Android-only size cap for the textures the Quest materials use. Earlier records are kept for Remove().
            var log = new Log { prefab = prefab, quest_prefab = questPath, made_at = DateTime.UtcNow.ToString("o") };
            if (earlier != null) log.textures.AddRange(earlier.textures);
            foreach (Texture texture in mainTextures)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                if (android.overridden && android.maxTextureSize <= AndroidMaxSize) continue;
                var change = new TextureChange { texture = path, before_overridden = android.overridden, before_max_size = android.maxTextureSize, before_format = (int)android.format };
                android.overridden = true;
                android.maxTextureSize = Math.Min(AndroidMaxSize, importer.maxTextureSize);
                if (!change.before_overridden) android.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
                if (!log.textures.Any(t => t.texture == path)) log.textures.Add(change);
                result.Textures.Add(change);
            }
            UndoRecord.Write("quest_version", prefab, JsonUtility.ToJson(log, true));
            AssetDatabase.SaveAssets();
            result.QuestPrefab = questPath;
            return result;
        }

        /// <summary>Deletes the Quest version and puts back the texture settings it changed; returns how many were restored.</summary>
        public static int Remove(string prefab)
        {
            SyncIds(prefab);
            int restored = 0;
            Log log = ReadLog(prefab);
            if (log != null)
                foreach (TextureChange change in log.textures)
                {
                    if (!(AssetImporter.GetAtPath(change.texture) is TextureImporter importer)) continue;
                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    android.overridden = change.before_overridden;
                    android.maxTextureSize = change.before_max_size;
                    android.format = (TextureImporterFormat)change.before_format;
                    importer.SetPlatformTextureSettings(android);
                    importer.SaveAndReimport();
                    restored++;
                }
            AssetDatabase.DeleteAsset(FolderFor(prefab));
            UndoRecord.Delete("quest_version", prefab);
            return restored;
        }

        public static Log ReadLog(string prefab)
        {
            string path = LogPath(prefab);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<Log>(File.ReadAllText(path)); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>
        /// Keeps one VRChat avatar ID for both versions, so the PC and Quest uploads go to the same avatar. Give it either
        /// the avatar prefab or its Quest version. When only one has an ID, the other gets it. Returns a note when the two
        /// have different IDs (left as they are), otherwise null.
        /// </summary>
        public static string SyncIds(string prefab)
        {
            string pc = IsQuestVersion(prefab) ? PcPrefabFor(prefab) : prefab;
            string quest = IsQuestVersion(prefab) ? prefab : QuestPrefabFor(prefab);
            if (pc == null || quest == null) return null;
            string pcId = IdOf(pc), questId = IdOf(quest);
            if (pcId == questId) return null;
            if (string.IsNullOrEmpty(pcId)) { SetId(pc, questId); return null; }
            if (string.IsNullOrEmpty(questId)) { SetId(quest, pcId); return null; }
            return "The PC and Quest versions point to different VRChat avatars (" + pcId + " and " + questId + "); left as they are.";
        }

        public static string IdOf(string prefab)
        {
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(prefab)?.GetComponent<PipelineManager>();
            return manager ? manager.blueprintId ?? "" : "";
        }

        static void SetId(string prefab, string id)
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefab))
            {
                var manager = scope.prefabContentsRoot.GetComponent<PipelineManager>();
                if (manager) manager.blueprintId = id;
            }
        }

        struct SavedLook
        {
            public Color? Color;
            public Texture Texture;
            public Vector2 Scale, Offset;
            public bool Transparent;
            public bool CutOut;
        }

        /// <summary>Colour, main texture and transparency from the material's saved data (works when its shader is missing).</summary>
        static SavedLook ReadSavedLook(Material material)
        {
            var look = new SavedLook { Scale = Vector2.one };
            var serialized = new SerializedObject(material);
            SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            foreach (string name in TextureNames)
            {
                SerializedProperty entry = Find(textures, name);
                Texture texture = entry?.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                if (texture == null) continue;
                look.Texture = texture;
                look.Scale = entry.FindPropertyRelative("m_Scale").vector2Value;
                look.Offset = entry.FindPropertyRelative("m_Offset").vector2Value;
                break;
            }
            SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
            foreach (string name in ColorNames)
            {
                SerializedProperty entry = Find(colors, name);
                if (entry == null) continue;
                Color color = entry.colorValue;
                color.a = 1f;   // Quest's Toon Standard is opaque
                look.Color = color;
                break;
            }
            int queue = serialized.FindProperty("m_CustomRenderQueue").intValue;
            if (queue < 0 && material.shader != null && !AvatarPreflight.ShaderMissing(material)) queue = material.shader.renderQueue;
            SerializedProperty blend = Find(serialized.FindProperty("m_SavedProperties.m_Floats"), "_BlendMode");
            if (blend != null)
            {
                // MToon: 0 opaque, 1 cut-out, 2 transparent, 3 transparent with depth write.
                look.Transparent = blend.floatValue >= 2f;
                look.CutOut = Mathf.Approximately(blend.floatValue, 1f);
            }
            else
            {
                look.Transparent = queue > 2500;
                look.CutOut = queue >= 2450 && queue <= 2500;
            }
            return look;
        }

        /// <summary>True when the visible pixels of a texture are mostly bright (a highlight), read from its source file.</summary>
        public static bool MostlyBright(Texture texture)
        {
            string path = texture ? AssetDatabase.GetAssetPath(texture) : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var image = new Texture2D(2, 2);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path))) return false;
                double light = 0, weight = 0;
                Color32[] pixels = image.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 7)
                {
                    float alpha = pixels[i].a / 255f;
                    light += alpha * (0.2126 * pixels[i].r + 0.7152 * pixels[i].g + 0.0722 * pixels[i].b) / 255.0;
                    weight += alpha;
                }
                return weight > 0 && light / weight > 0.75;
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        /// <summary>The value ("second") of a saved-properties entry, by name ("first").</summary>
        static SerializedProperty Find(SerializedProperty array, string name)
        {
            if (array == null || !array.isArray) return null;
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty pair = array.GetArrayElementAtIndex(i);
                if (pair.FindPropertyRelative("first")?.stringValue == name) return pair.FindPropertyRelative("second");
            }
            return null;
        }

        static void ForEachRenderer(string prefab, Action<Renderer[]> action)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            if (asset == null) return;
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                action(instance.GetComponentsInChildren<Renderer>(true));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
