using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BedrockForge.Bedrock;
using BedrockForge.EditorTools.UI;
using BedrockForge.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BedrockForge.EditorTools
{
    /// <summary>
    /// Main window (Tools → Bedrock Forge): lists the worlds of the installed game, opens one in the Scene view
    /// and shows the editing panel. The world lives as hidden objects in the open scene; nothing is saved into it.
    /// </summary>
    public sealed class BedrockForgeWindow : EditorWindow
    {
        private const string StylePath = "Assets/BedrockForge/Editor/UI/BedrockForge.uss";

        private WorldView? _view;
        private WorldEditorController? _controller;
        private EditorPanel? _panel;
        private string _search = "";
        private Dimension _dimension = Dimension.Overworld;
        private int _radius = 16;
        private readonly List<Texture2D> _icons = new List<Texture2D>();

        [MenuItem("Tools/Bedrock Forge %#b", priority = 0)]
        public static BedrockForgeWindow Open()
        {
            var window = GetWindow<BedrockForgeWindow>();
            window.titleContent = new GUIContent("Bedrock Forge", EditorGUIUtility.IconContent("d_Terrain Icon").image);
            window.minSize = new Vector2(420, 400);
            return window;
        }

        public WorldView? View => _view;
        public WorldEditorController? Controller => _controller;
        public EditorPanel? Panel => _panel;

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += CloseWorld;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= CloseWorld;
            CloseWorld();
            ClearIcons();
        }

        private void CreateGUI()
        {
            ShowWorldList();
        }

        private void Tick()
        {
            if (_view == null) return;
            bool uploading = _view.HasPendingUploads;
            _view.Tick();
            if (uploading)
            {
                SceneView.RepaintAll();
                _panel?.Refresh();
            }
        }

        // ---------------------------------------------------------------- world list

        public void ShowWorldList()
        {
            CloseWorld();
            var root = rootVisualElement;
            root.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null && !root.styleSheets.Contains(style))
                root.styleSheets.Add(style);

            var page = Ui.Add(new VisualElement(), "cc-panel");
            root.Add(page);

            var header = Ui.Add(new VisualElement(), "cc-header");
            var titles = new VisualElement();
            titles.Add(Ui.Add(new Label("Bedrock Forge"), "cc-title"));
            titles.Add(Ui.Add(new Label("World editor for Minecraft Bedrock"), "cc-subtitle"));
            header.Add(titles);
            header.Add(Ui.Button("Refresh", ShowWorldList));
            page.Add(header);

            var scroll = Ui.Add(new ScrollView(ScrollViewMode.Vertical), "cc-content");
            page.Add(scroll);

            var options = new VisualElement();
            options.Add(Ui.Row("Search", Ui.Text(_search, v => { _search = v; ShowWorldList(); })));
            var dimension = new EnumField(_dimension);
            dimension.RegisterValueChangedCallback(e => _dimension = (Dimension)e.newValue);
            options.Add(Ui.Row("Dimension", dimension));
            options.Add(Ui.Row("Load radius (chunks)", Ui.Int(_radius, v => _radius = Mathf.Clamp(v, 2, 64))));
            options.Add(Ui.Buttons(
                Ui.Button("Import .mcworld…", ImportMcWorld),
                Ui.Button("Open folder…", OpenFolder),
                Ui.Button("Shape gallery", OpenGallery)));
            scroll.Add(options);

            List<WorldSummary> worlds;
            try
            {
                worlds = WorldLibrary.FindWorlds();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                scroll.Add(Ui.Warning("Cannot list worlds: " + ex.Message));
                return;
            }

            var shown = worlds.Where(w => _search.Length == 0 || w.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            scroll.Add(Ui.Section($"Your worlds ({shown.Count})"));
            if (worlds.Count == 0)
                scroll.Add(Ui.Hint("No worlds found. Is Minecraft Bedrock for Windows installed? You can also open a folder or import a .mcworld."));

            ClearIcons();
            foreach (var world in shown)
                scroll.Add(WorldCard(world));

            scroll.Add(Ui.Hint("\"Open a copy\" creates a new world next to the original (visible in the game): the safest way to experiment. " +
                               "Close the world in Minecraft before saving."));
        }

        private VisualElement WorldCard(WorldSummary world)
        {
            var card = Ui.Add(new VisualElement(), "cc-card");

            var icon = Ui.Add(new Image { scaleMode = ScaleMode.ScaleAndCrop }, "cc-card-icon");
            if (world.IconPath != null)
            {
                var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (tex.LoadImage(File.ReadAllBytes(world.IconPath)))
                {
                    icon.image = tex;
                    _icons.Add(tex);
                }
            }
            card.Add(icon);

            var info = Ui.Add(new VisualElement(), "cc-card-info");
            info.Add(Ui.Add(new Label(world.Name), "cc-card-title"));
            string mode = world.GameType switch { 0 => "Survival", 1 => "Creative", 2 => "Adventure", 6 => "Spectator", _ => "?" };
            string played = world.LastPlayed?.ToString("dd/MM/yyyy HH:mm") ?? "never";
            info.Add(Ui.Add(new Label($"{mode}  ·  {world.SizeBytes / (1024.0 * 1024.0):0.#} MB  ·  last played {played}"), "cc-subtitle"));
            info.Add(Ui.Add(new Label($"{world.Source}  ·  {world.FolderName}"), "cc-card-path"));

            if (world.IsEncrypted)
            {
                info.Add(Ui.Warning("Marketplace world (encrypted): cannot be opened."));
            }
            else
            {
                info.Add(Ui.Buttons(
                    Ui.Button("Open a copy", () => OpenCopy(world), "cc-button--primary"),
                    Ui.Button("Open original", () => OpenWorld(world.Folder)),
                    Ui.Button("Folder", () => EditorUtility.RevealInFinder(Path.Combine(world.Folder, "level.dat")))));
            }
            card.Add(info);
            return card;
        }

        private void ClearIcons()
        {
            foreach (var t in _icons)
                if (t != null) DestroyImmediate(t);
            _icons.Clear();
        }

        private void OpenCopy(WorldSummary world)
        {
            try
            {
                string copy = WorldLibrary.CopyWorld(world.Folder);
                OpenWorld(copy);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                EditorUtility.DisplayDialog("Bedrock Forge", "Copy failed: " + ex.Message, "OK");
            }
        }

        private void ImportMcWorld()
        {
            string file = EditorUtility.OpenFilePanel("Import a world", "", "mcworld,zip");
            if (string.IsNullOrEmpty(file)) return;
            var roots = WorldLibrary.FindWorldRoots();
            if (roots.Count == 0)
            {
                EditorUtility.DisplayDialog("Bedrock Forge", "Minecraft worlds folder not found.", "OK");
                return;
            }
            try
            {
                OpenWorld(WorldLibrary.ImportMcWorld(file, roots[0].path));
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Bedrock Forge", "Import failed: " + ex.Message, "OK");
            }
        }

        private void OpenFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Bedrock world folder (with level.dat)", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!File.Exists(Path.Combine(folder, "level.dat")))
            {
                EditorUtility.DisplayDialog("Bedrock Forge", "This folder has no level.dat.", "OK");
                return;
            }
            OpenWorld(folder);
        }

        // ---------------------------------------------------------------- editing

        public void OpenWorld(string folder)
        {
            ShowEditor();
            _view!.Open(folder, _dimension);
        }

        public void OpenGallery()
        {
            ShowEditor();
            _view!.OpenGallery();
            _panel!.Refresh();
        }

        private void ShowEditor()
        {
            CloseWorld();
            _view = new WorldView { ViewRadius = _radius };
            _controller = new WorldEditorController(_view);
            _view.Loaded += () => _panel?.Refresh();

            var root = rootVisualElement;
            root.Clear();
            _panel = new EditorPanel(_controller, AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath));
            _panel.BackToWorldList += () =>
            {
                if (_controller?.Session?.HasChanges == true &&
                    !EditorUtility.DisplayDialog("Bedrock Forge", "Some changes are not saved. Discard them?", "Discard", "Cancel"))
                    return;
                ShowWorldList();
            };
            root.Add(_panel);

            // The Scene view is where the world is shown and edited.
            var sceneView = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            sceneView.Focus();
            sceneView.wantsMouseMove = true;
            sceneView.sceneViewState.alwaysRefresh = false;
            Focus();
        }

        private void CloseWorld()
        {
            _controller?.Dispose();
            _controller = null;
            _view?.Dispose();
            _view = null;
            _panel = null;
            SceneView.RepaintAll();
        }
    }
}
