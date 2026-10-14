using System;
using System.IO;
using BedrockForge.EditorTools.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace BedrockForge.EditorTools
{
    /// <summary>
    /// Screenshots of the Bedrock Forge window and the Scene view, for the README and for checking changes.
    /// Run Unity (not in batch mode, it needs a screen) with
    /// <c>-executeMethod BedrockForge.EditorTools.DevCapture.Run</c> and these arguments:
    /// <c>-bfWorld &lt;folder|gallery&gt;</c>, <c>-bfOut &lt;folder&gt;</c>, optional <c>-bfTab World|Player|Mobs|Block|Data</c>,
    /// <c>-bfSelect entity</c>, or <c>-bfList 1</c> to capture the world list instead. Unity quits when done.
    /// </summary>
    public static class DevCapture
    {
        private static double _readyAt;
        private static BedrockForgeWindow? _window;

        public static void Run()
        {
            _window = BedrockForgeWindow.Open();
            _window.position = new Rect(40, 60, 520, 980);

            string world = Arg("-bfWorld") ?? "";
            if (Arg("-bfList") == "1")
                _window.ShowWorldList();
            else if (world == "gallery")
                _window.OpenGallery();
            else if (world.Length > 0)
                _window.OpenWorld(world);

            _readyAt = 0;
            EditorApplication.update += WaitThenCapture;
        }

        private static void WaitThenCapture()
        {
            var view = _window?.View;
            bool loaded = view == null || (!view.IsLoading && !view.HasPendingUploads && view.ChunksShown >= view.ChunksTotal);
            if (!loaded)
                return;

            if (_readyAt == 0)
            {
                _readyAt = EditorApplication.timeSinceStartup + 2.0;
                if (_window?.Panel is { } panel)
                {
                    if (_window.Controller is { } c && Arg("-bfSelect") == "entity" && c.Entities.Count > 0)
                        c.SelectEntity(c.Entities[0], focusCamera: true);
                    if (Enum.TryParse(Arg("-bfTab") ?? "", true, out EditorPanel.Tab tab))
                        panel.ShowTab(tab);
                }
                _window?.Repaint();
                SceneView.RepaintAll();
                return;
            }
            if (EditorApplication.timeSinceStartup < _readyAt)
                return;

            EditorApplication.update -= WaitThenCapture;
            string output = Arg("-bfOut") ?? Path.GetFullPath("../docs/images");
            Directory.CreateDirectory(output);
            string suffix = Arg("-bfTab") ?? (Arg("-bfList") == "1" ? "list" : "world");

            try
            {
                if (_window != null)
                    Save(_window.position, Path.Combine(output, $"forge-window-{suffix}.png"));
                if (SceneView.lastActiveSceneView is { } sceneView)
                    Save(sceneView.position, Path.Combine(output, $"forge-scene-{suffix}.png"));
                Debug.Log($"[Bedrock Forge] Captures saved to {output}");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Batch-mode check of everything except drawing the windows: lists the worlds, opens one (-bfWorld),
        /// places a block, builds every panel tab and renders the world to -bfOut/forge-batch.png.
        /// </summary>
        public static void BatchSmokeTest()
        {
            string world = Arg("-bfWorld") ?? throw new ArgumentException("-bfWorld is required");
            string output = Arg("-bfOut") ?? Path.GetFullPath("../docs/images");
            Debug.Log($"[Bedrock Forge] {Bedrock.WorldLibrary.FindWorlds().Count} installed worlds found.");

            var view = new Rendering.WorldView();
            var controller = new WorldEditorController(view);
            view.Open(world, Bedrock.Dimension.Overworld);
            double deadline = EditorApplication.timeSinceStartup + 120;

            void Step()
            {
                view.Tick();
                bool done = !view.IsLoading && !view.HasPendingUploads && view.ChunksTotal > 0 && view.ChunksShown >= view.ChunksTotal;
                if (!done && EditorApplication.timeSinceStartup < deadline)
                    return;
                EditorApplication.update -= Step;
                try
                {
                    if (!done) throw new TimeoutException("World did not load: " + view.Status);
                    Debug.Log($"[Bedrock Forge] Loaded {view.ChunksShown} chunks, {view.BlocksShown:N0} blocks, {controller.Entities.Count} entities, {view.TexturesInfo}.");

                    var p = view.FocusPoint;
                    var target = new Vector3Int((int)p.x, (int)p.y + 1, (int)p.z);
                    controller.SetBlock(target, controller.Session!.CreateState("gold_block"));
                    Debug.Log($"[Bedrock Forge] SetBlock: {controller.LastMessage} (pending {controller.Session.PendingChanges})");

                    var panel = new EditorPanel(controller, AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/BedrockForge/Editor/UI/BedrockForge.uss"));
                    if (controller.Entities.Count > 0) controller.SelectEntity(controller.Entities[0]);
                    controller.SelectBlock(target);
                    foreach (EditorPanel.Tab tab in Enum.GetValues(typeof(EditorPanel.Tab)))
                    {
                        panel.ShowTab(tab);
                        Debug.Log($"[Bedrock Forge] Tab {tab}: {CountElements(panel)} elements");
                    }

                    // Render the hidden world objects with a temporary camera looking at the focus point.
                    var go = new GameObject("Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
                    var cam = go.AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.53f, 0.71f, 1f);
                    cam.farClipPlane = 1000;
                    var focus = Rendering.World.VoxelRaycast.ToUnity(p);
                    go.transform.position = focus + new Vector3(-30, 25, 30);
                    go.transform.LookAt(focus);
                    var rt = new RenderTexture(1600, 900, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    RenderTexture.active = null;
                    Directory.CreateDirectory(output);
                    File.WriteAllBytes(Path.Combine(output, "forge-batch.png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(go);

                    controller.Dispose();
                    view.Dispose();
                    Debug.Log("[Bedrock Forge] Smoke test passed.");
                    EditorApplication.Exit(0);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    EditorApplication.Exit(1);
                }
            }

            EditorApplication.update += Step;
        }

        private static int CountElements(UnityEngine.UIElements.VisualElement e)
        {
            int n = 1;
            foreach (var child in e.Children()) n += CountElements(child);
            return n;
        }

        private static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Save(Rect screenRect, string path)
        {
            float scale = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(screenRect.width * scale);
            int height = Mathf.RoundToInt(screenRect.height * scale);
            var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(screenRect.x * scale, screenRect.y * scale), width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
