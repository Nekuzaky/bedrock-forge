using System;
using System.Collections.Generic;
using BedrockForge.Bedrock;
using UnityEngine;

namespace BedrockForge.Rendering.Blocks
{
    /// <summary>
    /// Block categories and flat fallback colors, used when a texture is missing or no resource pack is available.
    /// Lookup order: exact name, dyed blocks, ores, wood types, material keywords, then a stable hash color.
    /// </summary>
    public static class BlockStyles
    {
        private static readonly HashSet<string> Invisible = new HashSet<string>
        {
            "air", "cave_air", "void_air", "structure_void", "barrier", "light_block", "moving_block", "border_block",
        };

        private static readonly string[] PlantWords =
        {
            "sapling", "tulip", "short_grass", "tall_grass", "fern", "dandelion", "poppy", "orchid", "allium", "azure_bluet",
            "oxeye_daisy", "cornflower", "lily_of_the_valley", "wither_rose", "torch", "dead_bush", "deadbush", "seagrass",
            "kelp", "vine", "sugar_cane", "reeds", "wheat", "carrots", "potatoes", "beetroot", "sweet_berry_bush",
            "rose_bush", "lilac", "peony", "sunflower", "double_plant", "pink_petals", "sea_pickle", "web", "ladder",
            "lever", "button", "flower", "bush", "roots", "sprouts", "fungus", "lantern", "chain", "lichen",
            "hanging_moss", "dripleaf", "spore_blossom", "nether_wart", "bamboo_sapling", "cave_vines", "pitcher",
            "torchflower", "firefly_bush", "wildflowers", "cactus_flower", "amethyst_bud", "amethyst_cluster", "fire",
        };

        private static readonly string[] FlatWords = { "snow_layer", "carpet", "rail", "redstone_wire", "pressure_plate", "waterlily", "lily_pad", "moss_carpet", "leaf_litter" };

        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>
        {
            ["stone"] = "7F7F7F", ["cobblestone"] = "7A7A7A", ["mossy_cobblestone"] = "6E7F5E", ["smooth_stone"] = "9E9E9E",
            ["stone_bricks"] = "7A7A7A", ["stone_brick"] = "7A7A7A", ["stonebrick"] = "7A7A7A", ["mossy_stone_bricks"] = "6F7A63",
            ["deepslate"] = "50505A", ["cobbled_deepslate"] = "4D4D52", ["tuff"] = "6C6D66", ["calcite"] = "DFE0DC",
            ["granite"] = "9A6B57", ["diorite"] = "BDBDBD", ["andesite"] = "888889", ["dripstone_block"] = "866B5C",
            ["dirt"] = "866043", ["coarse_dirt"] = "77553B", ["rooted_dirt"] = "90684C", ["farmland"] = "6F4A2E",
            ["mud"] = "3C393D", ["packed_mud"] = "8E6B50", ["mud_bricks"] = "89674F", ["clay"] = "A0A6B3",
            ["gravel"] = "857F7E", ["sand"] = "DBCFA3", ["red_sand"] = "BE6621", ["suspicious_sand"] = "D6C99C",
            ["sandstone"] = "D8CB9B", ["red_sandstone"] = "B6612A", ["bedrock"] = "555555",
            ["snow"] = "F9FEFE", ["powder_snow"] = "F4F9FA", ["packed_ice"] = "8DB4FA", ["blue_ice"] = "74A7FD",
            ["obsidian"] = "0F0B19", ["crying_obsidian"] = "220A3D", ["netherrack"] = "6F3634", ["soul_sand"] = "513E32",
            ["soul_soil"] = "4B3A2F", ["glowstone"] = "F8D88B", ["magma"] = "8E3F1E", ["basalt"] = "515155",
            ["blackstone"] = "2A2328", ["end_stone"] = "DBDE9E", ["purpur_block"] = "A97DA9", ["quartz_block"] = "ECE6DF",
            ["prismarine"] = "63A397", ["sea_lantern"] = "ACC7BE", ["hardened_clay"] = "985E43", ["terracotta"] = "985E43",
            ["hay_block"] = "A88B13", ["pumpkin"] = "C67415", ["carved_pumpkin"] = "C67415", ["lit_pumpkin"] = "D88A1E",
            ["melon_block"] = "6F9122", ["cactus"] = "5A8B2F", ["bookshelf"] = "6C5735", ["crafting_table"] = "76573A",
            ["furnace"] = "6E6E6E", ["lit_furnace"] = "6E6E6E", ["chest"] = "A06D2E", ["barrel"] = "8A6538",
            ["moss_block"] = "596E2D", ["sculk"] = "0D1E24", ["amethyst_block"] = "8562C2", ["honeycomb_block"] = "E5941E",
            ["slime"] = "70C05A", ["tnt"] = "C33F2C", ["brick_block"] = "966355", ["bricks"] = "966355",
            ["nether_brick"] = "2C1619", ["red_nether_brick"] = "450709", ["ancient_debris"] = "5F4038",
            ["iron_block"] = "DCDCDC", ["gold_block"] = "F6D03D", ["diamond_block"] = "62DBD5", ["emerald_block"] = "2BCB59",
            ["lapis_block"] = "1F43A0", ["redstone_block"] = "AF1806", ["coal_block"] = "101010", ["copper_block"] = "C06C50",
            ["grass_path"] = "9A8447", ["dirt_path"] = "9A8447", ["mycelium"] = "6F6265", ["podzol"] = "5B3F20",
            ["mushroom_stem"] = "CBC4B9", ["brown_mushroom_block"] = "957051", ["red_mushroom_block"] = "C72D2B",
            ["short_grass"] = "6A9F3A", ["tallgrass"] = "6A9F3A", ["tall_grass"] = "6A9F3A", ["fern"] = "5E8F35",
            ["seagrass"] = "3E8A2E", ["kelp"] = "4F8C2A", ["sugar_cane"] = "89B65C", ["reeds"] = "89B65C",
            ["torch"] = "FFD15A", ["vine"] = "4F7F21", ["wheat"] = "C9A83A", ["dead_bush"] = "7B5B2A", ["deadbush"] = "7B5B2A",
            ["poppy"] = "E01010", ["red_flower"] = "E01010", ["dandelion"] = "F5D01E", ["yellow_flower"] = "F5D01E",
            ["red_mushroom"] = "C93030", ["brown_mushroom"] = "9A7553", ["web"] = "DADADA", ["cobweb"] = "DADADA",
            ["water"] = "3F76E4", ["flowing_water"] = "3F76E4", ["lava"] = "D96415", ["flowing_lava"] = "D96415",
            ["glass"] = "C8E4EE", ["ice"] = "91B7FD", ["snow_layer"] = "F9FEFE", ["rail"] = "8A7D6A",
            ["leaf_litter"] = "A0743B", ["bush"] = "5E8F35", ["firefly_bush"] = "5A7A30", ["glow_lichen"] = "7FA08A",
            ["rose_bush"] = "C2353A", ["lilac"] = "C79BD1", ["azure_bluet"] = "E2E8F0", ["oxeye_daisy"] = "EEEFD8",
            ["cornflower"] = "4C6FE0", ["fire"] = "F28C1E", ["smooth_basalt"] = "48484E", ["budding_amethyst"] = "7C57B5",
            ["raw_copper_block"] = "9A6A4F", ["raw_iron_block"] = "A6876B", ["raw_gold_block"] = "DDA72E",
            ["mob_spawner"] = "1E2A35", ["bee_nest"] = "C9A05A", ["beehive"] = "B48A4E",
        };

        private static readonly (string name, string hex)[] Dyes =
        {
            ("light_blue", "3AAFD9"), ("light_gray", "8E8E86"), ("silver", "8E8E86"), ("white", "F0F0F0"), ("orange", "F07613"),
            ("magenta", "BD44B3"), ("yellow", "F8C527"), ("lime", "70B919"), ("pink", "ED8DAC"), ("gray", "3E4447"),
            ("cyan", "158991"), ("purple", "792AAC"), ("blue", "35399D"), ("brown", "724728"), ("green", "546D1B"),
            ("red", "A12722"), ("black", "141519"),
        };

        private static readonly string[] DyedWords = { "wool", "concrete", "carpet", "terracotta", "stained_glass", "shulker_box", "candle", "bed", "glazed" };

        private static readonly (string wood, string bark, string inner, string planks, string leaves)[] Woods =
        {
            ("dark_oak", "3C2F1A", "4F3218", "42291A", "3B6B1F"),
            ("pale_oak", "5E5552", "E3D9D6", "E3D9D6", "8A9188"),
            ("oak", "6D5533", "B08E5A", "A2834F", "4F8F2B"),
            ("spruce", "3B2810", "73583A", "72542F", "3B5B39"),
            ("birch", "D8D7D2", "C5B47F", "C0AF79", "6B8F45"),
            ("jungle", "56461A", "AB8456", "A07351", "3F8B1E"),
            ("acacia", "676157", "AE5D3B", "A85A32", "4A7A1E"),
            ("mangrove", "54443A", "773632", "773632", "4E7A21"),
            ("cherry", "37202D", "E2B2AD", "E2B2AD", "E8A6C2"),
            ("bamboo", "7F8A2F", "C2AE58", "C2AE58", "6E8A2A"),
            ("crimson", "5C1A1E", "653047", "653047", "8A1B1B"),
            ("warped", "3A3B4E", "2B6963", "2B6963", "167E86"),
            ("azalea", "6D5533", "B08E5A", "A2834F", "5E7A2E"),
        };

        private static readonly (string ore, string hex)[] Ores =
        {
            ("coal", "2F2F2F"), ("iron", "D8AF93"), ("copper", "C46F4E"), ("gold", "F5D44A"), ("redstone", "D8220F"),
            ("emerald", "29C35C"), ("lapis", "2F55C0"), ("diamond", "5DECE0"), ("quartz", "E8E1D6"),
        };

        private static readonly (string word, string hex)[] Materials =
        {
            ("deepslate", "50505A"), ("blackstone", "2A2328"), ("sandstone", "D8CB9B"), ("prismarine", "63A397"),
            ("purpur", "A97DA9"), ("quartz", "ECE6DF"), ("nether_brick", "2C1619"), ("end_stone", "DBDE9E"),
            ("mud_brick", "89674F"), ("tuff", "6C6D66"), ("stone_brick", "7A7A7A"), ("cobblestone", "7A7A7A"),
            ("brick", "966355"), ("granite", "9A6B57"), ("diorite", "BDBDBD"), ("andesite", "888889"),
            ("weathered_copper", "6C9B70"), ("exposed_copper", "A1806A"), ("oxidized_copper", "52A285"), ("copper", "C06C50"),
            ("resin", "D9631C"), ("stone", "7F7F7F"), ("glass", "C8E4EE"), ("ice", "91B7FD"), ("coral", "C55B8A"),
        };

        private static readonly string[] ShapeSuffixes =
        {
            "_double_slab", "_slab", "_stairs", "_wall", "_fence_gate", "_fence", "_pressure_plate", "_button",
            "_trapdoor", "_door", "_planks", "_log", "_wood", "_stem", "_hyphae",
        };

        public static BlockStyle For(BlockState state)
        {
            string name = state.Name.StartsWith("minecraft:", StringComparison.Ordinal) ? state.Name.Substring(10) : state.Name;

            if (Invisible.Contains(name) || name.StartsWith("light_block", StringComparison.Ordinal))
                return BlockStyle.Air;

            if (name == "water" || name == "flowing_water" || name == "bubble_column")
                return Uniform(BlockKind.Liquid, Hex("3F76E4"), 0.65f, 1);
            if (name == "lava" || name == "flowing_lava")
                return Uniform(BlockKind.Liquid, Hex("D96415"), 1f, 2);

            if (name.Contains("glass"))
                return Uniform(BlockKind.Transparent, ColorFor(name), 0.4f);
            if (name == "ice")
                return Uniform(BlockKind.Transparent, Hex("91B7FD"), 0.75f);

            if (ContainsAny(name, FlatWords))
                return Uniform(BlockKind.Flat, ColorFor(name));

            if (name.Contains("leaves"))
                return Uniform(BlockKind.Solid, LeavesColor(name));

            if (IsPlant(name))
                return Uniform(BlockKind.Plant, ColorFor(name));

            if (name == "grass_block" || name == "grass")
                return Faces(Hex("7CBD52"), Hex("8B6B47"), Hex("866043"));
            if (name == "mycelium")
                return Faces(Hex("6F6265"), Hex("866043"), Hex("866043"));
            if (name == "podzol")
                return Faces(Hex("5B3F20"), Hex("866043"), Hex("866043"));

            foreach (var w in Woods)
            {
                if (!name.Contains(w.wood))
                    continue;
                if (name.EndsWith("_log", StringComparison.Ordinal) || name.EndsWith("_stem", StringComparison.Ordinal))
                    return Faces(Hex(w.inner), Hex(w.bark), Hex(w.inner));
                if (name.EndsWith("_wood", StringComparison.Ordinal) || name.EndsWith("_hyphae", StringComparison.Ordinal))
                    return Uniform(BlockKind.Solid, Hex(w.bark));
            }

            return Uniform(BlockKind.Solid, ColorFor(name));
        }

        private static bool IsPlant(string name)
        {
            if (name == "grass_block" || name == "sea_lantern" || name.EndsWith("_block", StringComparison.Ordinal) || name.Contains("coral_block"))
                return false;
            if (name.Contains("coral") || name.Contains("mushroom") && !name.Contains("stem"))
                return true;
            return ContainsAny(name, PlantWords);
        }

        private static Color ColorFor(string name)
        {
            if (Exact.TryGetValue(name, out string hex))
                return Hex(hex);

            if (ContainsAny(name, DyedWords))
            {
                foreach (var d in Dyes)
                {
                    if (!name.StartsWith(d.name + "_", StringComparison.Ordinal))
                        continue;
                    var c = Hex(d.hex);
                    // Terracotta is the same palette, but earthy and darker.
                    return name.Contains("terracotta") && !name.Contains("glazed") ? Color.Lerp(c, Hex("985E43"), 0.5f) * 0.85f : c;
                }
            }

            if (name.Contains("_ore"))
            {
                var host = name.Contains("deepslate") ? Hex("50505A") : name.Contains("nether") ? Hex("6F3634") : Hex("7F7F7F");
                foreach (var o in Ores)
                    if (name.Contains(o.ore))
                        return Color.Lerp(host, Hex(o.hex), 0.3f);
                return host;
            }

            foreach (var w in Woods)
            {
                if (!name.Contains(w.wood))
                    continue;
                if (name.Contains("leaves"))
                    return Hex(w.leaves);
                if (name.Contains("sapling") || name.Contains("propagule"))
                    return Hex(w.leaves);
                return Hex(w.planks);
            }

            string bare = StripShape(name);
            if (bare != name && Exact.TryGetValue(bare, out hex))
                return Hex(hex);
            if (bare.StartsWith("polished_", StringComparison.Ordinal) && Exact.TryGetValue(bare.Substring(9), out hex))
                return Hex(hex) * 1.05f;

            foreach (var m in Materials)
                if (name.Contains(m.word))
                    return Hex(m.hex);

            return HashColor(name);
        }

        private static Color LeavesColor(string name)
        {
            foreach (var w in Woods)
                if (name.Contains(w.wood))
                    return Hex(w.leaves);
            return Hex("4F8F2B");
        }

        private static string StripShape(string name)
        {
            foreach (var s in ShapeSuffixes)
                if (name.EndsWith(s, StringComparison.Ordinal))
                    return name.Substring(0, name.Length - s.Length);
            return name;
        }

        private static bool ContainsAny(string name, string[] words)
        {
            foreach (var w in words)
                if (name.Contains(w))
                    return true;
            return false;
        }

        /// <summary>Stable muted color for blocks not in the tables, so they stay distinguishable.</summary>
        private static Color HashColor(string name)
        {
            uint h = 2166136261;
            foreach (char ch in name)
                h = (h ^ ch) * 16777619;
            float hue = (h % 360) / 360f;
            return Color.HSVToRGB(hue, 0.35f, 0.65f).linear;
        }

        private static BlockStyle Uniform(BlockKind kind, Color c, float alpha = 1f, byte liquidGroup = 0) =>
            new BlockStyle(kind, c, c, c, alpha, liquidGroup);

        private static BlockStyle Faces(Color top, Color side, Color bottom) =>
            new BlockStyle(BlockKind.Solid, top, side, bottom);

        /// <summary>Parses an sRGB hex color and converts it to linear for vertex colors.</summary>
        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c.linear;
        }
    }
}
