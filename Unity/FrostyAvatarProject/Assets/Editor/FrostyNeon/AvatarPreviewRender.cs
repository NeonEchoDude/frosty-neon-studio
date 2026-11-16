// Frosty Neon avatar preview render (Editor-only).
//
// Renders an imported model or prefab to PNG files so its materials can be checked by eye without
// opening the Unity Editor. It uses Unity's preview renderer on a temporary copy: no asset, import
// setting or scene is changed. The picture shows the rest pose under two plain lights; it is not
// how VRChat lights an avatar in a world.
//
// Batch mode (Editor closed; do not pass -nographics):
//   Unity.exe -batchmode -projectPath <project> -executeMethod FrostyNeon.Editor.AvatarPreviewRender.Run
//       -previewAsset Assets/Path/Avatar.fbx [-previewOutDir Logs/FrostyNeon/previews] [-previewSize 1024] -logFile <log>
//   Writes <name>_front.png, <name>_three_quarter.png and <name>_back.png. Exit code 0 = written, 1 = failed.
//
// Measure() gives the facts the Avatar Guide shows beside the pictures: height, where VRChat puts the wearer's view
// (the avatar descriptor's view position) and the lowest point (how far the avatar sits above or below the floor).
// Render(..., markEyeLevel: true) draws the view position into the front and three-quarter pictures in neon cyan.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace FrostyNeon.Editor
{
    public static class AvatarPreviewRender
    {
        static readonly (string name, float yaw)[] Views = { ("front", 0f), ("three_quarter", 35f), ("back", 180f) };

        public static void Run()
        {
            int code = 1;
            try
            {
                string asset = Argument("-previewAsset");
                if (string.IsNullOrEmpty(asset))
                    throw new ArgumentException("Pass -previewAsset <Assets/...> (a model or prefab in the project).");
                string outDir = Argument("-previewOutDir") ?? "Logs/FrostyNeon/previews";
                int size = int.TryParse(Argument("-previewSize"), out int parsed) ? Mathf.Clamp(parsed, 128, 4096) : 1024;
                foreach (string path in Render(asset, outDir, size))
                    Debug.Log("Frosty Neon preview written to " + path);
                code = 0;
            }
            catch (Exception error)
            {
                Debug.LogError("Frosty Neon preview render failed: " + error);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        /// <summary>A dashed line across the picture at the point's height and a ring around the point.</summary>
        static void DrawMark(Texture2D image, Vector3 viewport)
        {
            if (viewport.z <= 0) return;
            int w = image.width, h = image.height;
            int cx = Mathf.RoundToInt(viewport.x * w), cy = Mathf.RoundToInt(viewport.y * h);
            int thick = Mathf.Max(1, w / 400);
            for (int x = 0; x < w; x++)
                if ((x / (w / 64 + 1)) % 2 == 0)
                    for (int d = -thick; d <= thick; d++) Set(image, x, cy + d);
            int radius = Mathf.Max(4, w / 60);
            for (int a = 0; a < 360; a++)
                for (int d = -thick; d <= thick; d++)
                    Set(image, cx + Mathf.RoundToInt((radius + d) * Mathf.Cos(a * Mathf.Deg2Rad)), cy + Mathf.RoundToInt((radius + d) * Mathf.Sin(a * Mathf.Deg2Rad)));
            image.Apply();
        }

        static void Set(Texture2D image, int x, int y)
        {
            if (x >= 0 && y >= 0 && x < image.width && y < image.height) image.SetPixel(x, y, MarkColor);
        }

        public struct Facts
        {
            public float Height;        // metres, lowest to highest point of the visible meshes
            public float Lowest;        // metres; below 0 = sinks into the floor, above 0 = floats
            public float ViewHeight;    // metres above the floor (avatar origin) where VRChat puts the wearer's eyes
            public bool HasView;
        }

        /// <summary>Height, lowest point and VRChat view height of a model or avatar prefab, measured on a temporary copy.</summary>
        public static Facts Measure(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) throw new FileNotFoundException("No model or prefab at " + assetPath);
            UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                var facts = new Facts();
                // Measured on the mesh vertices in the rest pose: a skinned mesh's bounds are padded and can sit
                // several centimetres below the real feet (found on FrostyNeonBot, 2026-11-15).
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (Renderer renderer in renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy))
                {
                    Mesh mesh = null;
                    Matrix4x4 toWorld;
                    bool baked = false;
                    if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    {
                        mesh = new Mesh();
                        skinned.BakeMesh(mesh, true);
                        baked = true;
                        toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        mesh = filter ? filter.sharedMesh : null;
                        toWorld = renderer.transform.localToWorldMatrix;
                    }
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        float y = toWorld.MultiplyPoint3x4(vertex).y;
                        if (y < lowest) lowest = y;
                        if (y > highest) highest = y;
                    }
                    if (baked) UnityEngine.Object.DestroyImmediate(mesh);
                }
                if (highest >= lowest)
                {
                    facts.Height = highest - lowest;
                    facts.Lowest = lowest;
                }
                var descriptor = instance.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null)
                {
                    facts.HasView = true;
                    facts.ViewHeight = instance.transform.TransformPoint(descriptor.ViewPosition).y;
                }
                return facts;
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Plain-language notes about the facts, for the Guide. Empty when nothing looks off.</summary>
        public static System.Collections.Generic.List<string> Notes(Facts facts)
        {
            var notes = new System.Collections.Generic.List<string>();
            if (facts.Height <= 0) return notes;
            if (facts.Lowest < -0.03f) notes.Add("The lowest point is " + Cm(-facts.Lowest) + " below the floor, so the avatar may stand partly in the floor (usually fine if that is the soles of the shoes).");
            if (facts.Lowest > 0.03f) notes.Add("The lowest point is " + Cm(facts.Lowest) + " above the floor, so the avatar may float.");
            if (facts.HasView && (facts.ViewHeight < facts.Lowest + facts.Height * 0.5f || facts.ViewHeight > facts.Lowest + facts.Height * 1.02f))
                notes.Add("Your view (the cyan mark) is not near the head, so you would see from the wrong place in VRChat.");
            return notes;
        }

        static string Cm(float metres) => Mathf.RoundToInt(metres * 100) + " cm";

        /// <summary>Renders the views and returns the full paths of the PNG files written.</summary>
        public static string[] Render(string assetPath, string outDir, int size) => Render(assetPath, outDir, size, false);

        public static readonly Color MarkColor = new Color(0.243f, 0.949f, 1f, 1f);   // Frosty Neon cyan

        /// <summary>Renders the views; with markEyeLevel, the avatar's VRChat view position is drawn on the front and side views.</summary>
        public static string[] Render(string assetPath, string outDir, int size, bool markEyeLevel)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new FileNotFoundException("No model or prefab at " + assetPath);
            string fullDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(fullDir);

            var preview = new PreviewRenderUtility();
            try
            {
                GameObject instance = preview.InstantiatePrefabInScene(prefab);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    throw new InvalidOperationException("The model has no renderers to draw.");
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);

                Camera camera = preview.camera;
                camera.fieldOfView = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.32f, 0.33f, 0.36f, 1f);
                float radius = bounds.extents.magnitude;
                float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 1.5f);
                camera.farClipPlane = distance + radius * 1.5f;
                preview.ambientColor = new Color(0.35f, 0.35f, 0.38f, 1f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[1].intensity = 0.5f;

                string stem = Path.GetFileNameWithoutExtension(assetPath);
                var descriptor = instance.GetComponent<VRCAvatarDescriptor>();
                Vector3? eye = markEyeLevel && descriptor != null ? instance.transform.TransformPoint(descriptor.ViewPosition) : (Vector3?)null;
                return Views.Select(view =>
                {
                    // The model faces +Z, so the front view looks back along -Z.
                    Quaternion rotation = Quaternion.Euler(8f, 180f + view.yaw, 0f);
                    camera.transform.rotation = rotation;
                    camera.transform.position = bounds.center - rotation * Vector3.forward * distance;
                    preview.lights[0].transform.rotation = rotation * Quaternion.Euler(30f, -30f, 0f);
                    preview.lights[1].transform.rotation = rotation * Quaternion.Euler(-20f, 140f, 0f);

                    preview.BeginStaticPreview(new Rect(0, 0, size, size));
                    camera.Render();
                    Texture2D image = preview.EndStaticPreview();
                    if (eye.HasValue && view.name != "back") DrawMark(image, camera.WorldToViewportPoint(eye.Value));
                    string path = Path.Combine(fullDir, stem + "_" + view.name + ".png");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    return path;
                }).ToArray();
            }
            finally
            {
                preview.Cleanup();
            }
        }
    }
}
