using UnityEngine;

namespace BedrockForge.Rendering.Blocks
{
    public enum BlockKind : byte
    {
        /// <summary>Not rendered (air, barriers, structure voids…).</summary>
        Air,
        /// <summary>Full opaque cube, hides its neighbours' faces and casts ambient occlusion.</summary>
        Solid,
        /// <summary>Full see-through cube (glass, ice). Hides faces only against the same block.</summary>
        Transparent,
        /// <summary>Water or lava: lowered top, faces hidden against the same liquid.</summary>
        Liquid,
        /// <summary>Two crossed quads (flowers, grass, torches…).</summary>
        Plant,
        /// <summary>Thin 1/8 slab on the floor (snow layer, carpet, rails…).</summary>
        Flat,
    }

    /// <summary>Category and fallback colors of a block state. Colors are in linear space, ready for vertex colors.</summary>
    public readonly struct BlockStyle
    {
        public readonly BlockKind Kind;
        public readonly Color Top;
        public readonly Color Side;
        public readonly Color Bottom;
        public readonly float Alpha;
        /// <summary>Liquids of the same group merge (1 = water, 2 = lava).</summary>
        public readonly byte LiquidGroup;

        public BlockStyle(BlockKind kind, Color top, Color side, Color bottom, float alpha = 1f, byte liquidGroup = 0)
        {
            Kind = kind;
            Top = top;
            Side = side;
            Bottom = bottom;
            Alpha = alpha;
            LiquidGroup = liquidGroup;
        }

        public bool IsSolid => Kind == BlockKind.Solid;

        public bool UsesTransparentMesh => Alpha < 0.999f;

        public static readonly BlockStyle Air = new BlockStyle(BlockKind.Air, Color.clear, Color.clear, Color.clear);
    }
}
