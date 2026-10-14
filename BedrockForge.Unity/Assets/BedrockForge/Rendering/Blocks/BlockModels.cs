using System;
using System.Collections.Generic;
using BedrockForge.Bedrock;
using BedrockForge.Nbt;
using BedrockForge.Resources;
using UnityEngine;

namespace BedrockForge.Rendering.Blocks
{
    /// <summary>Turns a block state into a <see cref="BlockVisual"/>: shape from its name and states, textures from the resource packs.</summary>
    public static class BlockModels
    {
        /// <summary>World block names whose blocks.json entry uses an older name.</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["grass_block"] = "grass",
            ["bricks"] = "brick_block",
        };

        private static readonly string[] ShapeSuffixes = { "_double_slab", "_slab", "_stairs", "_wall", "_fence_gate", "_fence" };

        // Bedrock tints these grayscale textures per biome. Plains-like defaults for now (sRGB).
        private static readonly Color GrassTint = Hex("91BD59");
        private static readonly Color FoliageTint = Hex("77AB2F");
        private static readonly Color WaterTint = Hex("44AFF5");

        public static BlockVisual Create(BlockState state, TextureCatalog catalog)
        {
            string name = state.Name.StartsWith("minecraft:", StringComparison.Ordinal) ? state.Name.Substring(10) : state.Name;
            var style = BlockStyles.For(state);
            if (style.Kind == BlockKind.Air)
                return BlockVisual.CreateAir();

            var v = new BlockVisual { Alpha = style.Alpha, LiquidGroup = style.LiquidGroup };
            SetShape(v, name, style, state.States);
            SetLayer(v, name, style);

            v.FullOpaque = v.Shape == BlockShape.Cube && v.Layer == RenderLayer.Opaque;
            v.CastsAo = v.FullOpaque || name.Contains("leaves");
            v.SelfCull = v.Shape == BlockShape.Cube && (v.Layer == RenderLayer.Transparent || name.Contains("glass"));

            SetTextures(v, name, style, state.States, catalog);
            return v;
        }

        private static void SetShape(BlockVisual v, string name, BlockStyle style, NbtCompound states)
        {
            switch (style.Kind)
            {
                case BlockKind.Liquid:
                    v.Shape = BlockShape.Liquid;
                    return;
                case BlockKind.Plant:
                    v.Shape = BlockShape.Cross;
                    return;
                case BlockKind.Flat:
                    v.Shape = BlockShape.Layer;
                    v.LayerHeight = name == "snow_layer" ? (Int(states, "height") + 1) / 8f : 1f / 16f;
                    return;
            }

            if (name.Contains("double_slab"))
                v.Shape = BlockShape.Cube;
            else if (name.Contains("slab"))
            {
                v.Shape = BlockShape.Slab;
                v.TopHalf = Str(states, "minecraft:vertical_half") == "top" || Int(states, "top_slot_bit") == 1;
            }
            else if (name.EndsWith("_stairs", StringComparison.Ordinal))
            {
                v.Shape = BlockShape.Stairs;
                v.StairFacing = Mathf.Clamp(Int(states, "weirdo_direction"), 0, 3);
                v.UpsideDown = Int(states, "upside_down_bit") == 1;
            }
            else if (name.Contains("fence"))
            {
                v.Shape = BlockShape.Fence;
                v.ConnectGroup = name.Contains("nether_brick") ? "nether_fence" : "fence";
            }
            else if (name.EndsWith("_wall", StringComparison.Ordinal))
            {
                v.Shape = BlockShape.Wall;
                v.ConnectGroup = "wall";
                v.WallSides[0] = WallSide(states, "east");
                v.WallSides[1] = WallSide(states, "west");
                v.WallSides[2] = WallSide(states, "south");
                v.WallSides[3] = WallSide(states, "north");
                v.WallPost = !states.ContainsKey("wall_post_bit") || Int(states, "wall_post_bit") == 1;
            }
            else if (name.Contains("pane") || name == "iron_bars")
            {
                v.Shape = BlockShape.Pane;
                v.ConnectGroup = "pane";
            }
            else
                v.Shape = BlockShape.Cube;
        }

        private static void SetLayer(BlockVisual v, string name, BlockStyle style)
        {
            if (v.Shape == BlockShape.Liquid)
                v.Layer = v.LiquidGroup == 1 ? RenderLayer.Transparent : RenderLayer.Opaque;
            else if (name.Contains("stained_glass") || name == "ice" || name == "slime" || name == "honey_block")
                v.Layer = RenderLayer.Transparent;
            else if (style.Kind == BlockKind.Transparent || name.Contains("leaves") || v.Shape == BlockShape.Cross
                     || v.Shape == BlockShape.Layer || v.Shape == BlockShape.Pane)
                v.Layer = RenderLayer.Cutout;
            else
                v.Layer = RenderLayer.Opaque;

            // Fully transparent-blended blocks keep their style alpha; everything else is opaque per pixel.
            if (v.Layer != RenderLayer.Transparent)
                v.Alpha = 1f;
        }

        private static void SetTextures(BlockVisual v, string name, BlockStyle style, NbtCompound states, TextureCatalog catalog)
        {
            var fallback = new[] { style.Top, style.Bottom, style.Side, style.Side, style.Side, style.Side };
            var packs = catalog.Packs;
            string?[]? keys = packs == null ? null : FaceKeys(packs, name);

            for (int f = 0; f < 6; f++)
            {
                int slice = -1;
                if (keys?[f] is string key && packs!.ResolveTerrain(key) is { } texture)
                    slice = catalog.GetSlice(texture, GrayTint(name, texture.Path));

                v.Slices[f] = slice >= 0 ? slice : TextureCatalog.WhiteSlice;
                v.FaceColors[f] = slice >= 0 ? Color.white : fallback[f];
            }

            if (v.Shape == BlockShape.Cross)
            {
                // Plants draw one texture on both quads: the "up" entry is the plant itself.
                for (int f = 1; f < 6; f++)
                {
                    v.Slices[f] = v.Slices[0];
                    v.FaceColors[f] = v.FaceColors[0];
                }
            }

            RotatePillar(v, Str(states, "pillar_axis"));
        }

        private static string?[]? FaceKeys(ResourcePackStack packs, string name)
        {
            if (Aliases.TryGetValue(name, out var alias))
                name = alias;

            var keys = packs.GetBlockFaceKeys(name);
            if (keys != null)
                return keys;

            // No blocks.json entry: try a terrain texture of the same name, then the base material of stairs/slabs/walls.
            if (packs.HasTerrainTexture(name))
                return Same(name);

            foreach (var suffix in ShapeSuffixes)
            {
                if (!name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                string baseName = name.Substring(0, name.Length - suffix.Length);
                foreach (var candidate in new[] { baseName, baseName + "s", baseName + "_planks", baseName + "_block" })
                {
                    keys = packs.GetBlockFaceKeys(candidate);
                    if (keys != null)
                        return keys;
                    if (packs.HasTerrainTexture(candidate))
                        return Same(candidate);
                }
            }
            return null;
        }

        private static string?[] Same(string key) => new string?[] { key, key, key, key, key, key };

        /// <summary>Logs, basalt, pillars lying sideways: the end texture goes on the X or Z faces.</summary>
        private static void RotatePillar(BlockVisual v, string? axis)
        {
            if (axis != "x" && axis != "z")
                return;

            int end = v.Slices[0];
            int side = v.Slices[2];
            var endColor = v.FaceColors[0];
            var sideColor = v.FaceColors[2];
            int endA = axis == "x" ? 2 : 4, endB = axis == "x" ? 3 : 5;

            for (int f = 0; f < 6; f++)
            {
                bool isEnd = f == endA || f == endB;
                v.Slices[f] = isEnd ? end : side;
                v.FaceColors[f] = isEnd ? endColor : sideColor;
            }
        }

        private static Color? GrayTint(string name, string texturePath)
        {
            if (texturePath.Contains("water"))
                return WaterTint;
            if (name.Contains("leaves") || name == "vine" || name == "waterlily" || name == "lily_pad")
                return name.Contains("spruce") ? Hex("619961") : name.Contains("birch") ? Hex("80A755") : FoliageTint;
            if (name == "grass" || name == "grass_block" || name.Contains("short_grass") || name.Contains("tall_grass")
                || name.Contains("fern") || name == "bush" || name == "reeds" || name == "sugar_cane")
                return GrassTint;
            if (name == "leaf_litter")
                return Hex("A0743B");
            return null;
        }

        private static byte WallSide(NbtCompound states, string direction) =>
            Str(states, "wall_connection_type_" + direction) switch
            {
                "short" => 1,
                "tall" => 2,
                _ => 0,
            };

        private static string? Str(NbtCompound states, string key) => states.TryGetValue(key, out var v) ? v as string : null;

        private static int Int(NbtCompound states, string key) =>
            states.TryGetValue(key, out var v) && v != null && !(v is string) ? Convert.ToInt32(v) : 0;

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
