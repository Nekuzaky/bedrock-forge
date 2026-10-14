using System;
using System.Collections.Generic;
using System.Linq;
using BedrockForge.Bedrock;
using BedrockForge.Nbt;
using BedrockForge.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;

namespace BedrockForge.EditorTools.UI
{
    /// <summary>
    /// The editing panel of the Bedrock Forge window: world settings, players, mobs, the selected block and every
    /// raw record of the world, plus the save button. Each tab is rebuilt from the session when shown.
    /// </summary>
    public sealed class EditorPanel : VisualElement
    {
        public enum Tab { World, Player, Mobs, Block, Data }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.World, "World"), (Tab.Player, "Player"), (Tab.Mobs, "Mobs"), (Tab.Block, "Block"), (Tab.Data, "Data"),
        };

        private static readonly (int, string)[] GameModes = { (0, "Survival"), (1, "Creative"), (2, "Adventure"), (6, "Spectator") };
        private static readonly (int, string)[] PlayerGameModes = { (0, "Survival"), (1, "Creative"), (2, "Adventure"), (5, "World default"), (6, "Spectator") };
        private static readonly (int, string)[] Difficulties = { (0, "Peaceful"), (1, "Easy"), (2, "Normal"), (3, "Hard") };
        private static readonly (int, string)[] Dimensions = { (0, "Overworld"), (1, "Nether"), (2, "End") };

        private static readonly string[] GameRules =
        {
            "commandblockoutput", "commandblocksenabled", "dodaylightcycle", "doentitydrops", "dofiretick", "doimmediaterespawn",
            "doinsomnia", "dolimitedcrafting", "domobloot", "domobspawning", "dotiledrops", "doweathercycle", "drowningdamage",
            "falldamage", "firedamage", "freezedamage", "keepinventory", "mobgriefing", "naturalregeneration",
            "projectilescanbreakblocks", "pvp", "recipesunlock", "respawnblocksexplode", "sendcommandfeedback",
            "showbordereffect", "showcoordinates", "showdaysplayed", "showdeathmessages", "showrecipemessages", "showtags",
            "spawnradius", "randomtickspeed", "functioncommandlimit", "maxcommandchainlength", "playerssleepingpercentage",
            "tntexplodes", "tntexplosiondropdecay",
        };

        private readonly WorldEditorController controller;
        private readonly VisualElement _tabRow;
        private readonly ScrollView _content;
        private readonly Label _subtitle;
        private readonly Label _status;
        private readonly Button _saveButton;
        private Tab _tab = Tab.World;
        private string _mobFilter = "";
        private string _dataFilter = "";
        private string _blockFilter = "";
        private byte[]? _selectedDataKey;

        public Tab CurrentTab => _tab;

        public event Action? BackToWorldList;

        public EditorPanel(WorldEditorController controller, StyleSheet? styleSheet)
        {
            this.controller = controller;
            if (styleSheet != null)
                styleSheets.Add(styleSheet);
            AddToClassList("cc-panel");

            var header = Ui.Add(new VisualElement(), "cc-header");
            var titles = new VisualElement();
            titles.Add(Ui.Add(new Label("Bedrock Forge"), "cc-title"));
            _subtitle = Ui.Add(new Label("Loading world…"), "cc-subtitle");
            titles.Add(_subtitle);
            header.Add(titles);
            header.Add(Ui.Button("← Worlds", () => BackToWorldList?.Invoke()));
            hierarchy.Add(header);

            _tabRow = Ui.Add(new VisualElement(), "cc-tabs");
            foreach (var (tab, label) in Tabs)
                _tabRow.Add(Ui.Add(new Button(() => ShowTab(tab)) { text = label, userData = tab }, "cc-tab"));
            hierarchy.Add(_tabRow);

            _content = Ui.Add(new ScrollView(ScrollViewMode.Vertical), "cc-content");
            hierarchy.Add(_content);

            var footer = Ui.Add(new VisualElement(), "cc-footer");
            _status = Ui.Add(new Label(""), "cc-status");
            footer.Add(_status);
            _saveButton = Ui.Button("Save", Save, "cc-button--primary");
            footer.Add(Ui.Buttons(_saveButton, Ui.Button("Discard all (reload)", Reload)));
            hierarchy.Add(footer);

            controller.SelectionChanged += OnSelectionChanged;
            controller.WorldChanged += OnWorldChanged;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                controller.SelectionChanged -= OnSelectionChanged;
                controller.WorldChanged -= OnWorldChanged;
            });

            ShowTab(_tab);
            UpdateFooter();
        }

        public void ShowTab(Tab tab)
        {
            _tab = tab;
            foreach (var button in _tabRow.Children().OfType<Button>())
                button.EnableInClassList("cc-tab--active", (Tab)button.userData == tab);
            RebuildContent();
        }

        /// <summary>Rebuilds the current tab, e.g. once a world finished loading.</summary>
        public void Refresh()
        {
            RebuildContent();
            UpdateFooter();
        }

        private void OnWorldChanged()
        {
            UpdateFooter();
        }

        private void UpdateFooter()
        {
            var session = controller.Session;
            _subtitle.text = session == null
                ? (controller.Viewer.IsLoading ? controller.Viewer.Status : "Shape gallery")
                : $"{session.World.Name}  ·  {controller.Viewer.Dimension}  ·  {controller.Viewer.ChunksShown}/{controller.Viewer.ChunksTotal} chunks";
            int pending = session?.PendingChanges ?? 0;
            _saveButton.text = pending == 0 ? "Save" : $"Save ({pending} changes)";
            _saveButton.SetEnabled(pending > 0);

            string message = controller.LastMessage;
            _status.text = message.Length > 0 ? message : "Close the world in Minecraft before saving. A .mcworld backup is made every time.";
            _status.EnableInClassList("cc-status--error", message.StartsWith("Failed", StringComparison.Ordinal));
        }

        private void OnSelectionChanged()
        {
            if (controller.SelectedEntity != null && _tab != Tab.Mobs) ShowTab(Tab.Mobs);
            else if (controller.SelectedBlock != null && _tab != Tab.Block) ShowTab(Tab.Block);
            else if (_tab == Tab.Mobs || _tab == Tab.Block || _tab == Tab.Player) RebuildContent();
            UpdateFooter();
        }

        private void Save()
        {
            controller.Save();
            UpdateFooter();
        }

        private void Reload()
        {
            controller.Viewer.Reload();
            RebuildContent();
            UpdateFooter();
        }

        private void RebuildContent()
        {
            _content.Clear();
            var session = controller.Session;
            if (session == null)
            {
                _content.Add(Ui.Hint(controller.Viewer.IsLoading
                    ? "Loading world…"
                    : "Shape gallery: nothing to edit here. Go back to the world list to open a world."));
                return;
            }

            switch (_tab)
            {
                case Tab.World: BuildWorldTab(session); break;
                case Tab.Player: BuildPlayerTab(session); break;
                case Tab.Mobs: BuildMobsTab(session); break;
                case Tab.Block: BuildBlockTab(session); break;
                case Tab.Data: BuildDataTab(session); break;
            }
        }

        private void Put(VisualElement element) => _content.Add(element);

        // ---------------------------------------------------------------- world (level.dat)

        private void BuildWorldTab(WorldEditSession session)
        {
            var dat = session.LevelDat;
            void Changed() { session.MarkLevelDatChanged(); UpdateFooter(); }

            Put(Ui.Section("General"));
            Put(Ui.Row("World name", Ui.Text(dat.GetString("LevelName") ?? session.World.Name, v => { dat["LevelName"] = v; Changed(); })));
            Put(Ui.Row("Game mode", Ui.Choice(dat.GetInt("GameType") ?? 0, GameModes, v => { dat["GameType"] = v; Changed(); })));
            Put(Ui.Row("Difficulty", Ui.Choice(dat.GetInt("Difficulty") ?? 2, Difficulties, v => { dat["Difficulty"] = v; Changed(); })));
            AddByteToggle(dat, "commandsEnabled", "Cheats (commands)", Changed);
            AddByteToggle(dat, "IsHardcore", "Hardcore", Changed);
            AddByteToggle(dat, "bonusChestEnabled", "Bonus chest", Changed);
            if (dat.TryGetValue("RandomSeed", out var seed) && seed is long s)
                Put(Ui.Row("Seed", Ui.Long(s, v => { dat["RandomSeed"] = v; Changed(); })));
            Put(Ui.Hint("Changing the seed only affects chunks that have not been generated yet."));

            Put(Ui.Section("Time and weather"));
            if (dat.TryGetValue("Time", out var time) && time is long t)
            {
                Put(Ui.Row("Time (ticks)", Ui.Long(t, v => { dat["Time"] = v; Changed(); })));
                long day = t - t % 24000;
                Put(Ui.Buttons(
                    Ui.Button("Morning", () => { dat["Time"] = day + 1000L; Changed(); RebuildContent(); }),
                    Ui.Button("Noon", () => { dat["Time"] = day + 6000L; Changed(); RebuildContent(); }),
                    Ui.Button("Evening", () => { dat["Time"] = day + 12000L; Changed(); RebuildContent(); }),
                    Ui.Button("Midnight", () => { dat["Time"] = day + 18000L; Changed(); RebuildContent(); })));
            }
            Put(Ui.Buttons(
                Ui.Button("Clear", () => { SetWeather(dat, 0f, 0f); Changed(); RebuildContent(); }),
                Ui.Button("Rain", () => { SetWeather(dat, 1f, 0f); Changed(); RebuildContent(); }),
                Ui.Button("Thunder", () => { SetWeather(dat, 1f, 1f); Changed(); RebuildContent(); })));
            AddFloat(dat, "rainLevel", "Rain level", Changed);
            AddFloat(dat, "lightningLevel", "Thunder level", Changed);

            Put(Ui.Section("World spawn"));
            int sx = dat.GetInt("SpawnX") ?? 0, sy = dat.GetInt("SpawnY") ?? 64, sz = dat.GetInt("SpawnZ") ?? 0;
            Put(Ui.Row("X Y Z", Ui.Xyz(sx, sy, sz, (x, y, z) =>
            {
                dat["SpawnX"] = Mathf.RoundToInt(x);
                dat["SpawnY"] = Mathf.RoundToInt(y);
                dat["SpawnZ"] = Mathf.RoundToInt(z);
                Changed();
            })));
            Put(Ui.Buttons(
                Ui.Button("Show", () => controller.FocusOn(new Vector3(sx, sy == 32767 ? 80 : sy, sz))),
                Ui.Button("Set to view center", () =>
                {
                    var c = controller.PivotMinecraftPosition;
                    dat["SpawnX"] = Mathf.FloorToInt(c.x);
                    dat["SpawnY"] = Mathf.FloorToInt(c.y);
                    dat["SpawnZ"] = Mathf.FloorToInt(c.z);
                    Changed();
                    RebuildContent();
                })));
            if (sy == 32767)
                Put(Ui.Hint("Y = 32767 means \"on the surface\", chosen by the game."));

            Put(Ui.Section("Game rules"));
            foreach (var rule in GameRules)
            {
                if (!dat.TryGetValue(rule, out var value)) continue;
                if (value is byte b)
                    Put(Ui.Row(rule, Ui.Bool(b != 0, v => { dat[rule] = (byte)(v ? 1 : 0); Changed(); })));
                else if (value is int i)
                    Put(Ui.Row(rule, Ui.Int(i, v => { dat[rule] = v; Changed(); })));
            }

            Put(Ui.Section("Full level.dat"));
            Put(Ui.Hint("Every value of the file, editable directly."));
            Put(new NbtEditor(dat, Changed));
        }

        private static void SetWeather(NbtCompound dat, float rain, float lightning)
        {
            dat["rainLevel"] = rain;
            dat["lightningLevel"] = lightning;
            dat["rainTime"] = rain > 0 ? 12000 : 120000;
            dat["lightningTime"] = lightning > 0 ? 12000 : 120000;
        }

        private void AddByteToggle(NbtCompound c, string key, string label, Action changed)
        {
            if (c.TryGetValue(key, out var v) && v is byte b)
                Put(Ui.Row(label, Ui.Bool(b != 0, x => { c[key] = (byte)(x ? 1 : 0); changed(); })));
        }

        private void AddFloat(NbtCompound c, string key, string label, Action changed)
        {
            if (c.TryGetValue(key, out var v) && v is float f)
                Put(Ui.Row(label, Ui.Float(f, x => { c[key] = x; changed(); })));
        }

        // ---------------------------------------------------------------- players

        private void BuildPlayerTab(WorldEditSession session)
        {
            var players = session.Players();
            if (players.Count == 0)
            {
                Put(Ui.Hint("No player saved in this world."));
                return;
            }

            var player = players.FirstOrDefault(p => controller.SelectedPlayer != null && p.Label == controller.SelectedPlayer.Label) ?? players[0];
            if (players.Count > 1)
            {
                var choice = new DropdownField(players.Select(p => p.Label).ToList(), players.IndexOf(player));
                choice.RegisterValueChangedCallback(_ => controller.SelectPlayer(players[choice.index]));
                Put(Ui.Row("Player", choice));
            }

            var nbt = player.Nbt;
            void Changed() { session.SavePlayer(player); UpdateFooter(); }

            Put(Ui.Section(player.Label));
            Put(Ui.Row("Game mode", Ui.Choice(nbt.GetInt("PlayerGameMode") ?? 5, PlayerGameModes, v => { nbt["PlayerGameMode"] = v; Changed(); })));
            Put(Ui.Row("Dimension", Ui.Choice(nbt.GetInt("DimensionId") ?? 0, Dimensions, v => { nbt["DimensionId"] = v; Changed(); })));

            AddPosition(nbt, Changed, feetOffset: -1.62f);
            AddAttribute(nbt, "minecraft:health", "Health", Changed);
            AddAttribute(nbt, "minecraft:player.hunger", "Hunger", Changed);
            AddAttribute(nbt, "minecraft:player.saturation", "Saturation", Changed);
            AddAttribute(nbt, "minecraft:player.level", "Level", Changed, mirrorInt: "PlayerLevel");
            if (nbt.TryGetValue("PlayerLevelProgress", out var progress) && progress is float pr)
                Put(Ui.Row("XP progress (0–1)", Ui.Float(pr, v => { nbt["PlayerLevelProgress"] = Mathf.Clamp01(v); Changed(); })));

            if (nbt.Get<NbtCompound>("abilities") is { } abilities)
            {
                Put(Ui.Section("Abilities"));
                foreach (var key in abilities.Keys.OrderBy(k => k).ToList())
                {
                    if (abilities[key] is byte b)
                        Put(Ui.Row(key, Ui.Bool(b != 0, v => { abilities[key] = (byte)(v ? 1 : 0); Changed(); })));
                    else if (abilities[key] is float f)
                        Put(Ui.Row(key, Ui.Float(f, v => { abilities[key] = v; Changed(); })));
                }
            }

            AddInventory(nbt, "Inventory", "Inventory", 36, Changed);
            AddInventory(nbt, "Armor", "Armor", 4, Changed);
            AddInventory(nbt, "Offhand", "Offhand", 1, Changed);
            AddInventory(nbt, "EnderChestInventory", "Ender chest", 27, Changed);

            Put(Ui.Section("All player data"));
            Put(new NbtEditor(nbt, Changed));
        }

        private void AddPosition(NbtCompound nbt, Action changed, float feetOffset)
        {
            if (!(nbt.Get<NbtList>("Pos") is { Count: 3 } pos) || !(pos[0] is float x && pos[1] is float y && pos[2] is float z))
                return;
            Put(Ui.Section("Position"));
            Put(Ui.Row("X Y Z", Ui.Xyz(x, y, z, (nx, ny, nz) => { pos[0] = nx; pos[1] = ny; pos[2] = nz; changed(); })));
            Put(Ui.Buttons(
                Ui.Button("Show", () => controller.FocusOn(new Vector3(x, y + feetOffset, z))),
                Ui.Button("Move to view center", () =>
                {
                    var c = controller.PivotMinecraftPosition;
                    pos[0] = c.x;
                    pos[1] = c.y - feetOffset;
                    pos[2] = c.z;
                    changed();
                    RebuildContent();
                })));
        }

        /// <summary>Edits the "Current" value of an entry of the Attributes list (health, hunger, level…).</summary>
        private void AddAttribute(NbtCompound nbt, string name, string label, Action changed, string? mirrorInt = null)
        {
            var attribute = nbt.Get<NbtList>("Attributes")?.OfType<NbtCompound>().FirstOrDefault(a => a.GetString("Name") == name);
            if (attribute == null || !(attribute.TryGetValue("Current", out var cur) && cur is float current))
                return;
            float max = attribute.TryGetValue("Max", out var m) && m is float mf ? mf : float.MaxValue;
            Put(Ui.Row(max < 1e6f ? $"{label} (max {max:0})" : label, Ui.Float(current, v =>
            {
                attribute["Current"] = v;
                if (v > max) attribute["Max"] = v;
                if (mirrorInt != null) nbt[mirrorInt] = Mathf.RoundToInt(v);
                changed();
            })));
        }

        private void AddInventory(NbtCompound nbt, string key, string label, int slots, Action changed)
        {
            if (!(nbt.Get<NbtList>(key) is { } list))
                return;
            Put(Ui.Section(label));
            Put(new InventoryView(list, slots, changed));
        }

        // ---------------------------------------------------------------- mobs

        private void BuildMobsTab(WorldEditSession session)
        {
            var c = controller;
            var selected = c.SelectedEntity;
            if (selected != null)
                BuildEntityDetails(selected);

            Put(Ui.Section($"Mobs and entities ({c.Entities.Count})"));
            Put(Ui.Row("Filter (Enter)", Ui.Text(_mobFilter, v => { _mobFilter = v; RebuildContent(); })));
            Put(Ui.Buttons(Ui.Button("Reload list", () => { c.RefreshEntities(); RebuildContent(); })));

            var camera = c.PivotMinecraftPosition;
            var shown = c.Entities
                .Where(e => _mobFilter.Length == 0 || e.Identifier.IndexOf(_mobFilter, StringComparison.OrdinalIgnoreCase) >= 0
                            || (e.Nbt.GetString("CustomName") ?? "").IndexOf(_mobFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => (e, d: Vector3.Distance(camera, ToVector(e))))
                .OrderBy(t => t.d)
                .Take(200)
                .ToList();

            foreach (var (entity, distance) in shown)
            {
                string name = entity.Nbt.GetString("CustomName") is { Length: > 0 } custom ? $"\"{custom}\" " : "";
                var item = Ui.Add(new Button(() => c.SelectEntity(entity, focusCamera: true))
                {
                    text = $"{name}{Short(entity.Identifier)}   ·   {distance:0} m",
                }, "cc-list-item");
                if (entity == selected) item.AddToClassList("cc-list-item--selected");
                Put(item);
            }
            if (shown.Count == 0)
                Put(Ui.Hint("No entities. Mobs are only saved in chunks the game has already loaded."));
        }

        private void BuildEntityDetails(EntityRef entity)
        {
            var c = controller;
            var nbt = entity.Nbt;
            void Changed() { c.SaveEntity(entity); UpdateFooter(); }

            Put(Ui.Section(Short(entity.Identifier)));
            Put(Ui.Row("Type", new Label(entity.Identifier)));
            Put(Ui.Row("Custom name", Ui.Text(nbt.GetString("CustomName") ?? "", v =>
            {
                if (v.Length == 0) { nbt.Remove("CustomName"); nbt.Remove("CustomNameVisible"); }
                else { nbt["CustomName"] = v; nbt["CustomNameVisible"] = (byte)1; }
                Changed();
            })));
            AddPosition(nbt, Changed, feetOffset: 0f);
            AddAttribute(nbt, "minecraft:health", "Health", Changed);
            AddByteToggle(nbt, "IsBaby", "Baby", Changed);
            AddByteToggle(nbt, "IsTamed", "Tamed", Changed);
            AddByteToggle(nbt, "Persistent", "Never despawns", Changed);
            AddByteToggle(nbt, "Invulnerable", "Invulnerable", Changed);
            AddByteToggle(nbt, "Saddled", "Saddled", Changed);
            AddByteToggle(nbt, "Sheared", "Sheared", Changed);

            AddInventory(nbt, "Mainhand", "Main hand", 1, Changed);
            AddInventory(nbt, "Offhand", "Offhand", 1, Changed);
            AddInventory(nbt, "Armor", "Armor", 4, Changed);
            AddInventory(nbt, "ChestItems", "Carried chest", 15, Changed);
            AddInventory(nbt, "Items", "Items", 0, Changed);

            Put(Ui.Buttons(
                Ui.Button("Delete this mob", () => { c.DeleteSelectedEntity(); RebuildContent(); }, "cc-button--danger"),
                Ui.Button("Deselect", c.ClearSelection)));

            var all = Ui.Add(new Foldout { text = "All entity data", value = false }, "cc-nbt-foldout");
            bool built = false;
            all.RegisterValueChangedCallback(e =>
            {
                if (e.target != all || !e.newValue || built) return;
                built = true;
                all.Add(new NbtEditor(nbt, Changed));
            });
            Put(all);
        }

        private static Vector3 ToVector(EntityRef e) => e.Position is { } p ? new Vector3(p.x, p.y, p.z) : Vector3.zero;

        // ---------------------------------------------------------------- blocks

        private void BuildBlockTab(WorldEditSession session)
        {
            var c = controller;
            var dim = c.Viewer.Dimension;

            Put(Ui.Section("Tool"));
            Put(Ui.Buttons(
                Ui.Button("Select", () => c.Tool = EditTool.Select),
                Ui.Button("Break", () => c.Tool = EditTool.Break),
                Ui.Button("Place", () => c.Tool = EditTool.Place)));
            Put(Ui.Hint("In the Scene view: left click acts, Ctrl+click picks the block under the cursor, the toolbar at the bottom switches tools."));

            if (c.SelectedBlock is { } p)
            {
                var state = session.GetBlock(p.x, p.y, p.z, dim);
                Put(Ui.Section($"Block at {p.x} {p.y} {p.z}"));
                Put(Ui.Row("Block", new Label(state.Name)));
                Put(Ui.Buttons(
                    Ui.Button("Break", () => c.SetBlock(p, session.Air), "cc-button--danger"),
                    Ui.Button("Use for placing", () => { c.PlaceState = state; c.Tool = EditTool.Place; })));

                if (state.States.Count > 0)
                {
                    Put(Ui.Hint("Block states (orientation, color…): each change replaces the block."));
                    var states = state.States.Clone();
                    Put(new NbtEditor(states, () => c.SetBlock(p, new BlockState(state.Name, states.Clone(), state.Version))));
                }

                var chunk = new ChunkPos(p.x >> 4, p.z >> 4, dim);
                var blockEntities = session.BlockEntities(chunk);
                var be = blockEntities.FirstOrDefault(e => e.GetInt("x") == p.x && e.GetInt("y") == p.y && e.GetInt("z") == p.z);
                if (be != null)
                {
                    void Changed() { session.SaveBlockEntities(chunk, blockEntities); UpdateFooter(); }
                    Put(Ui.Section($"Contents ({be.GetString("id")})"));
                    if (be.Get<NbtList>("Items") is { } items)
                        Put(new InventoryView(items, be.GetString("id") == "Hopper" ? 5 : 27, Changed));
                    Put(new NbtEditor(be, Changed));
                }
            }
            else
            {
                Put(Ui.Hint("With the Select tool, click a block to see its states and contents (chests, furnaces, signs…)."));
            }

            Put(Ui.Section("Block to place"));
            Put(Ui.Row("Current", new Label(c.PlaceState?.ToString() ?? "none")));
            var search = Ui.Text(_blockFilter, v => { _blockFilter = v; RebuildContent(); });
            Put(Ui.Row("Search", search));

            var worldStates = c.Viewer.Data?.Registry.AllStates() ?? new List<BlockState>();
            var seenNames = new HashSet<string>(worldStates.Select(s => s.Name));
            var packNames = c.Viewer.Catalog?.Packs?.BlockNames.Select(n => "minecraft:" + n) ?? Enumerable.Empty<string>();
            var names = seenNames.Concat(packNames).Distinct()
                .Where(n => _blockFilter.Length == 0 || n.IndexOf(_blockFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => seenNames.Contains(n) ? 0 : 1).ThenBy(n => n)
                .Take(60)
                .ToList();

            foreach (var name in names)
            {
                bool known = seenNames.Contains(name);
                var b = Ui.Add(new Button(() =>
                {
                    c.PlaceState = worldStates.FirstOrDefault(s => s.Name == name) ?? session.CreateState(name);
                    c.Tool = EditTool.Place;
                    RebuildContent();
                })
                {
                    text = known ? name.Replace("minecraft:", "") : name.Replace("minecraft:", "") + "   (default states)",
                }, "cc-list-item");
                Put(b);
            }
            Put(Ui.Hint("Blocks already present in the world keep their exact states. Others are created without states: " +
                        "if the block needs some (orientation, color…), the game may show it as an unknown block. " +
                        "Prefer Ctrl+click on an existing block."));
        }

        // ---------------------------------------------------------------- raw data

        private void BuildDataTab(WorldEditSession session)
        {
            Put(Ui.Hint("Every record of the world that is not a block: players, mobs, villages, scores, maps, portals…"));
            Put(Ui.Row("Filter", Ui.Text(_dataFilter, v => { _dataFilter = v; RebuildContent(); })));

            if (_selectedDataKey != null)
            {
                var key = _selectedDataKey;
                Put(Ui.Section(BedrockWorld.DescribeKey(key)));
                List<NbtCompound>? roots = null;
                try { roots = session.ReadNbtList(key); }
                catch (FormatException) { }

                if (roots == null || roots.Count == 0)
                {
                    int size = session.GetRaw(key)?.Length ?? 0;
                    Put(Ui.Hint($"Binary data ({size} bytes), not NBT: cannot be edited here."));
                }
                else
                {
                    void Changed() { session.WriteNbtList(key, roots); UpdateFooter(); }
                    for (int i = 0; i < roots.Count; i++)
                    {
                        if (roots.Count > 1) Add(Ui.Hint($"Item {i + 1}/{roots.Count}"));
                        Put(new NbtEditor(roots[i], Changed));
                    }
                }
                Put(Ui.Buttons(Ui.Button("Close", () => { _selectedDataKey = null; RebuildContent(); })));
            }

            var keys = session.Keys
                .Where(k => !ChunkKey.TryParse(k, out _))
                .Select(k => (key: k, label: BedrockWorld.DescribeKey(k)))
                .Where(t => _dataFilter.Length == 0 || t.label.IndexOf(_dataFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.label, StringComparer.Ordinal)
                .ToList();

            Put(Ui.Section($"Records ({keys.Count})"));
            foreach (var (key, label) in keys.Take(300))
            {
                var b = Ui.Add(new Button(() => { _selectedDataKey = key; RebuildContent(); _content!.scrollOffset = Vector2.zero; }) { text = label }, "cc-list-item");
                if (_selectedDataKey != null && key.SequenceEqual(_selectedDataKey)) b.AddToClassList("cc-list-item--selected");
                Put(b);
            }
            if (keys.Count > 300)
                Put(Ui.Hint($"… and {keys.Count - 300} more: refine the filter."));
        }

        private static string Short(string identifier)
        {
            int colon = identifier.IndexOf(':');
            return colon >= 0 ? identifier.Substring(colon + 1) : identifier;
        }
    }
}
