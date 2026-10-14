using UnityEngine;

namespace BedrockForge.Rendering.Blocks
{
    public enum BlockShape : byte
    {
        None,
        Cube,
        Slab,
        Stairs,
        Fence,
        Wall,
        Pane,
        Cross,
        /// <summary>Thin layer on the floor: snow layers, carpets, leaf litter, rails…</summary>
        Layer,
        Liquid,
    }

    public enum RenderLayer : byte
    {
        Opaque,
        /// <summary>Alpha-tested: leaves, plants, glass, panes.</summary>
        Cutout,
        /// <summary>Alpha-blended: water, stained glass, ice.</summary>
        Transparent,
    }

    /// <summary>Everything the mesher needs to draw one block state.</summary>
    public sealed class BlockVisual
    {
        public BlockShape Shape;
        public RenderLayer Layer;

        /// <summary>Full opaque cube: hides neighbouring faces.</summary>
        public bool FullOpaque;
        /// <summary>Darkens the corners of neighbouring faces (ambient occlusion).</summary>
        public bool CastsAo;
        /// <summary>Faces between two identical blocks are hidden (glass, ice…).</summary>
        public bool SelfCull;
        /// <summary>Liquids of the same group merge (1 = water, 2 = lava).</summary>
        public byte LiquidGroup;
        public float Alpha = 1f;

        /// <summary>Texture-array slice per face, order +Y, -Y, +X, -X, +Z, -Z. Slice 0 is plain white.</summary>
        public readonly int[] Slices = new int[6];
        /// <summary>Vertex color per face: white when textured, the fallback color otherwise.</summary>
        public readonly Color[] FaceColors = { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };

        // Shape parameters.
        public bool TopHalf;
        /// <summary>Side with the tall part: 0 = east (+X), 1 = west, 2 = south (+Z), 3 = north. Bedrock's weirdo_direction.</summary>
        public int StairFacing;
        public bool UpsideDown;
        public float LayerHeight = 1f / 16f;
        /// <summary>Wall arms east, west, south, north: 0 none, 1 short, 2 tall.</summary>
        public readonly byte[] WallSides = new byte[4];
        public bool WallPost = true;
        /// <summary>Fences connect to fences, panes to panes, walls to walls.</summary>
        public string? ConnectGroup;

        public bool IsAir => Shape == BlockShape.None;

        public static BlockVisual CreateAir() => new BlockVisual { Shape = BlockShape.None };
    }
}
