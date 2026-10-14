using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BedrockForge.Bedrock;
using BedrockForge.Rendering;
using BedrockForge.Rendering.World;
using UnityEditor;
using UnityEngine;

namespace BedrockForge.EditorTools
{
    public enum EditTool
    {
        /// <summary>Normal Unity scene navigation and selection.</summary>
        None,
        Select,
        Break,
        Place,
    }

    /// <summary>
    /// Editing in the Scene view: select blocks and entities, break and place blocks, pick a block with
    /// Ctrl+click. Draws the selection, the hovered block and entity markers with Handles. All edits go through
    /// the view's <see cref="WorldEditSession"/> and are written on <see cref="Save"/>.
    /// </summary>
    public sealed class WorldEditorController : IDisposable
    {
        public WorldView Viewer { get; }
        public EditTool Tool { get; set; } = EditTool.Select;
        public BlockState? PlaceState { get; set; }

        public Vector3Int? SelectedBlock { get; private set; }
        public EntityRef? SelectedEntity { get; private set; }
        public PlayerRef? SelectedPlayer { get; private set; }
        public IReadOnlyList<EntityRef> Entities => _entities;
        public WorldEditSession? Session => Viewer.Session;
        public string LastMessage { get; private set; } = "";
        public bool ShowEntities { get; set; } = true;

        public event Action? SelectionChanged;
        /// <summary>Raised when the world, its entity list or the pending-change count changes.</summary>
        public event Action? WorldChanged;

        private readonly List<EntityRef> _entities = new List<EntityRef>();
        private VoxelHit? _hover;
        private EntityRef? _hoverEntity;
        private Vector2 _pressPosition;
        private const float Reach = 300f;

        public static string BackupFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bedrock Forge", "Backups");

        public WorldEditorController(WorldView viewer)
        {
            Viewer = viewer;
            Viewer.Loaded += OnWorldLoaded;
            SceneView.duringSceneGui += OnSceneGui;
        }

        public void Dispose()
        {
            Viewer.Loaded -= OnWorldLoaded;
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void OnWorldLoaded()
        {
            if (Session != null)
                Session.Changed += () => WorldChanged?.Invoke();
            SelectedBlock = null;
            SelectedEntity = null;
            SelectedPlayer = null;
            LastMessage = "";
            RefreshEntities();
            SelectionChanged?.Invoke();
            WorldChanged?.Invoke();
            FocusOn(Viewer.FocusPoint, 40f);
        }

        // ---------------------------------------------------------------- scene view

        private void OnSceneGui(SceneView view)
        {
            if (Viewer.Data == null)
                return;

            var e = Event.current;
            DrawOverlays();
            DrawToolbar(view);

            if (Tool == EditTool.None)
                return;

            // Keep Unity from selecting scene objects while one of our tools is active.
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);

            if (e.type == EventType.KeyDown && !e.control && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Alpha1: Tool = EditTool.Select; e.Use(); break;
                    case KeyCode.Alpha2: Tool = EditTool.Break; e.Use(); break;
                    case KeyCode.Alpha3: Tool = EditTool.Place; e.Use(); break;
                    case KeyCode.Escape: ClearSelection(); e.Use(); break;
                    case KeyCode.Delete when SelectedEntity != null: DeleteSelectedEntity(); e.Use(); break;
                }
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.MouseUp)
            {
                var ray = VoxelRaycast.ToMinecraft(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                _hover = VoxelRaycast.Cast(Viewer.Data, ray, Reach, out var hit) ? hit : (VoxelHit?)null;
                _hoverEntity = PickEntity(ray, _hover?.Distance ?? float.MaxValue);
                if (e.type == EventType.MouseMove)
                    view.Repaint();

                // Alt = orbit, right/middle = Unity camera: only plain left clicks are ours.
                if (e.button == 0 && !e.alt)
                {
                    if (e.type == EventType.MouseDown)
                    {
                        _pressPosition = e.mousePosition;
                        GUIUtility.hotControl = control;
                        e.Use();
                    }
                    else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
                    {
                        GUIUtility.hotControl = 0;
                        if ((e.mousePosition - _pressPosition).sqrMagnitude < 36f)
                            Click(e.control || e.command);
                        e.Use();
                    }
                }
            }
        }

        private void Click(bool pick)
        {
            if (pick)
            {
                if (_hover is { } h && Session != null)
                {
                    PlaceState = Session.GetBlock(h.Block.x, h.Block.y, h.Block.z, Viewer.Dimension);
                    Tool = EditTool.Place;
                    LastMessage = $"Picked: {PlaceState}";
                    SelectionChanged?.Invoke();
                }
                return;
            }

            switch (Tool)
            {
                case EditTool.Select:
                    if (_hoverEntity != null) SelectEntity(_hoverEntity);
                    else if (_hover is { } hit) SelectBlock(hit.Block);
                    else ClearSelection();
                    break;
                case EditTool.Break when _hover is { } hit:
                    SetBlock(hit.Block, Session?.Air);
                    break;
                case EditTool.Place when _hover is { } hit:
                    if (PlaceState == null) LastMessage = "Pick a block first (Block tab, or Ctrl+click a block).";
                    else SetBlock(hit.Before, PlaceState);
                    break;
            }
        }

        private EntityRef? PickEntity(Ray ray, float maxDistance)
        {
            if (!ShowEntities) return null;
            EntityRef? best = null;
            float bestDistance = maxDistance;
            foreach (var entity in _entities)
            {
                var (min, max) = EntityBox(entity);
                var d = VoxelRaycast.IntersectBox(ray, min, max);
                if (d.HasValue && d.Value < bestDistance)
                {
                    bestDistance = d.Value;
                    best = entity;
                }
            }
            return best;
        }

        private void DrawOverlays()
        {
            if (ShowEntities)
            {
                foreach (var entity in _entities)
                {
                    bool selected = entity == SelectedEntity, hovered = entity == _hoverEntity;
                    var (min, max) = EntityBox(entity);
                    Handles.color = selected ? new Color(1f, 0.85f, 0.2f) : hovered ? Color.white : ColorFor(entity.Identifier);
                    DrawBox(min, max);
                    if (selected || hovered)
                        Handles.Label(VoxelRaycast.ToUnity(new Vector3((min.x + max.x) / 2, max.y + 0.4f, (min.z + max.z) / 2)), Label(entity), EditorStyles.whiteBoldLabel);
                }
            }

            if (SelectedBlock is { } sel)
            {
                Handles.color = Color.white;
                DrawBox(sel, sel + Vector3Int.one, 0.02f);
            }

            if (_hover is { } hover && Tool != EditTool.None && _hoverEntity == null)
            {
                var cell = Tool == EditTool.Place ? hover.Before : hover.Block;
                Handles.color = Tool == EditTool.Break ? new Color(1f, 0.3f, 0.3f) : Tool == EditTool.Place ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.5f);
                DrawBox(cell, cell + Vector3Int.one, 0.01f);
            }
        }

        private static void DrawBox(Vector3 min, Vector3 max, float pad = 0f)
        {
            var a = VoxelRaycast.ToUnity(min - Vector3.one * pad);
            var b = VoxelRaycast.ToUnity(max + Vector3.one * pad);
            Handles.DrawWireCube((a + b) / 2f, new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z)));
        }

        private void DrawToolbar(SceneView view)
        {
            Handles.BeginGUI();
            var rect = new Rect(10, view.position.height - 62, 560, 34);
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bedrock Forge", EditorStyles.boldLabel, GUILayout.Width(100));
            Tool = (EditTool)GUILayout.Toolbar((int)Tool, new[] { "Unity", "1 Select", "2 Break", "3 Place" }, GUILayout.Width(300));
            string info = Tool == EditTool.Place ? (PlaceState?.Name.Replace("minecraft:", "") ?? "no block") : Tool == EditTool.None ? "Unity navigation" : "Ctrl+click: pick block";
            GUILayout.Label(info, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ---------------------------------------------------------------- selection and camera

        public void SelectBlock(Vector3Int block)
        {
            SelectedBlock = block;
            SelectedEntity = null;
            SelectionChanged?.Invoke();
            SceneView.RepaintAll();
        }

        public void SelectEntity(EntityRef entity, bool focusCamera = false)
        {
            SelectedEntity = entity;
            SelectedBlock = null;
            if (focusCamera && entity.Position is { } p)
                FocusOn(new Vector3(p.x, p.y, p.z));
            SelectionChanged?.Invoke();
            SceneView.RepaintAll();
        }

        public void SelectPlayer(PlayerRef? player)
        {
            SelectedPlayer = player;
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            SelectedBlock = null;
            SelectedEntity = null;
            SelectionChanged?.Invoke();
            SceneView.RepaintAll();
        }

        /// <summary>Points the Scene view camera at a Minecraft position.</summary>
        public void FocusOn(Vector3 minecraftPosition, float size = 10f)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.LookAt(VoxelRaycast.ToUnity(minecraftPosition) + Vector3.up, Quaternion.Euler(30f, view.rotation.eulerAngles.y, 0f), size);
        }

        /// <summary>Scene camera position in Minecraft coordinates, e.g. to bring an entity or the player here.</summary>
        public Vector3 CameraMinecraftPosition
        {
            get
            {
                var view = SceneView.lastActiveSceneView;
                return view != null ? VoxelRaycast.ToMinecraft(view.camera.transform.position) : Vector3.zero;
            }
        }

        /// <summary>The point the Scene view orbits around, in Minecraft coordinates (better than the camera for placing things).</summary>
        public Vector3 PivotMinecraftPosition
        {
            get
            {
                var view = SceneView.lastActiveSceneView;
                return view != null ? VoxelRaycast.ToMinecraft(view.pivot) : Vector3.zero;
            }
        }

        // ---------------------------------------------------------------- edits

        public void SetBlock(Vector3Int p, BlockState? state)
        {
            if (Session == null || state == null)
                return;
            try
            {
                Session.SetBlock(p.x, p.y, p.z, state, Viewer.Dimension);
                Viewer.SetRenderedBlock(p.x, p.y, p.z, state);
                LastMessage = state.IsAir ? $"Block broken at {p.x} {p.y} {p.z}" : $"{state.Name} placed at {p.x} {p.y} {p.z}";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                LastMessage = ex.Message;
            }
            if (SelectedBlock == p)
                SelectionChanged?.Invoke();
            WorldChanged?.Invoke();
            SceneView.RepaintAll();
        }

        public void SaveEntity(EntityRef entity)
        {
            Session?.SaveEntity(entity);
            SceneView.RepaintAll();
        }

        public void DeleteSelectedEntity()
        {
            if (Session == null || SelectedEntity == null)
                return;
            Session.DeleteEntity(SelectedEntity);
            _entities.Remove(SelectedEntity);
            LastMessage = $"{SelectedEntity.Identifier} deleted";
            ClearSelection();
            WorldChanged?.Invoke();
        }

        /// <summary>Writes every pending change (the game must be closed). Returns a message for the UI.</summary>
        public string Save()
        {
            if (Session == null)
                return "No world open.";
            try
            {
                var result = Session.Save(BackupFolder);
                LastMessage = result.RecordsWritten == 0 && !result.LevelDatWritten
                    ? "Nothing to save."
                    : $"Saved: {result.RecordsWritten} record(s){(result.LevelDatWritten ? " + level.dat" : "")}. Backup: {Path.GetFileName(result.BackupPath)}";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LastMessage = "Failed: " + ex.Message;
            }
            WorldChanged?.Invoke();
            return LastMessage;
        }

        public void RefreshEntities()
        {
            _entities.Clear();
            if (Session == null)
                return;
            try
            {
                _entities.AddRange(Session.Entities().Where(e => e.Chunk.Dimension == Viewer.Dimension && e.Position != null));
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                LastMessage = "Unreadable entities: " + ex.Message;
            }
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- helpers

        private static string Label(EntityRef e)
        {
            string id = e.Identifier.Replace("minecraft:", "");
            return e.Nbt.GetString("CustomName") is { Length: > 0 } name ? $"{name} ({id})" : id;
        }

        private static Color ColorFor(string identifier)
        {
            uint h = 2166136261;
            foreach (char ch in identifier) h = (h ^ ch) * 16777619;
            return Color.HSVToRGB((h % 360) / 360f, 0.6f, 1f);
        }

        /// <summary>Approximate hit box in Minecraft coordinates. Pos is at the feet for mobs.</summary>
        public static (Vector3 min, Vector3 max) EntityBox(EntityRef entity)
        {
            var p = entity.Position ?? (0, 0, 0);
            var feet = new Vector3(p.x, p.y, p.z);
            return (feet + new Vector3(-0.4f, 0f, -0.4f), feet + new Vector3(0.4f, 1.2f, 0.4f));
        }
    }
}
