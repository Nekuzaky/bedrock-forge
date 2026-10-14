using BedrockForge.Bedrock;
using BedrockForge.Nbt;

namespace BedrockForge.Rendering.World
{
    /// <summary>A small hand-built scene showing every supported shape, to check models and textures without a world file.</summary>
    public static class ShapeGallery
    {
        public static ChunkPos Build(WorldData data)
        {
            void Set(int x, int y, int z, string name, params (string key, object value)[] states)
            {
                var nbt = new NbtCompound();
                foreach (var (key, value) in states)
                    nbt[key] = value;
                data.SetBlock(x, y, z, data.Registry.GetOrAdd(new BlockState("minecraft:" + name, nbt, 0)));
            }

            // Ground.
            for (int x = -2; x <= 17; x++)
                for (int z = -2; z <= 15; z++)
                {
                    Set(x, -1, z, "dirt");
                    Set(x, 0, z, "grass_block");
                }

            // Row 1: stairs facing east, west, south, north, then two upside-down.
            for (int d = 0; d < 4; d++)
                Set(1 + d * 2, 1, 1, "oak_stairs", ("weirdo_direction", d), ("upside_down_bit", (byte)0));
            Set(9, 1, 1, "stone_brick_stairs", ("weirdo_direction", 0), ("upside_down_bit", (byte)1));
            Set(11, 1, 1, "stone_brick_stairs", ("weirdo_direction", 2), ("upside_down_bit", (byte)1));
            Set(13, 1, 1, "cobblestone_stairs", ("weirdo_direction", 3), ("upside_down_bit", (byte)0));

            // Row 2: slabs and a staircase.
            Set(1, 1, 4, "smooth_stone_slab", ("minecraft:vertical_half", "bottom"));
            Set(3, 1, 4, "smooth_stone_slab", ("minecraft:vertical_half", "top"));
            Set(5, 1, 4, "oak_slab", ("minecraft:vertical_half", "bottom"));
            Set(7, 1, 4, "smooth_stone_double_slab", ("minecraft:vertical_half", "bottom"));
            for (int i = 0; i < 4; i++)
            {
                for (int y = 1; y <= i; y++)
                    Set(9 + i, y, 4, "stone_bricks");
                Set(9 + i, i + 1, 4, "stone_brick_stairs", ("weirdo_direction", 0), ("upside_down_bit", (byte)0));
            }

            // Row 3: fences with a corner, a wall using Bedrock's connection states, glass panes.
            for (int x = 1; x <= 5; x++)
                Set(x, 1, 7, "oak_fence");
            Set(5, 1, 8, "oak_fence");
            Set(5, 1, 9, "oak_fence");
            for (int x = 8; x <= 11; x++)
                Set(x, 1, 7, "cobblestone_wall",
                    ("wall_connection_type_east", x < 11 ? "short" : "none"),
                    ("wall_connection_type_west", x > 8 ? "short" : "none"),
                    ("wall_connection_type_north", "none"),
                    ("wall_connection_type_south", "none"),
                    ("wall_post_bit", (byte)(x == 8 || x == 11 ? 1 : 0)));
            for (int x = 13; x <= 15; x++)
                for (int y = 1; y <= 2; y++)
                    Set(x, y, 7, "glass_pane");

            // Row 4: logs on each axis, leaves, glass, plain cubes.
            Set(1, 1, 10, "oak_log", ("pillar_axis", "y"));
            Set(3, 1, 10, "oak_log", ("pillar_axis", "x"));
            Set(5, 1, 10, "birch_log", ("pillar_axis", "z"));
            for (int x = 7; x <= 8; x++)
                for (int y = 1; y <= 2; y++)
                    for (int z = 10; z <= 11; z++)
                        Set(x, y, z, "oak_leaves");
            Set(10, 1, 10, "glass");
            Set(11, 1, 10, "light_blue_stained_glass");
            Set(13, 1, 10, "stone");
            Set(14, 1, 10, "deepslate", ("pillar_axis", "y"));
            Set(15, 1, 10, "cobblestone");
            Set(13, 2, 10, "oak_planks");
            Set(15, 2, 10, "bricks");

            // Row 5: a pond, plants and thin layers.
            for (int x = 1; x <= 4; x++)
                for (int z = 12; z <= 14; z++)
                    Set(x, 0, z, "water");
            Set(7, 1, 13, "short_grass");
            Set(8, 1, 13, "poppy");
            Set(9, 1, 13, "dandelion");
            Set(10, 1, 13, "fern");
            Set(11, 1, 13, "snow_layer", ("height", 3), ("covered_bit", (byte)0));
            Set(12, 1, 13, "white_carpet");
            Set(13, 1, 13, "torch");
            Set(15, 1, 13, "iron_bars");
            Set(15, 1, 14, "iron_bars");

            return new ChunkPos(0, 0, Dimension.Overworld);
        }
    }
}
