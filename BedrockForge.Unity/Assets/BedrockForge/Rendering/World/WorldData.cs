#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BedrockForge.Bedrock;
using BedrockForge.Rendering.Blocks;

namespace BedrockForge.Rendering.World
{
    /// <summary>One 16-wide column of decoded sub-chunks, as global block ids in XZY order.</summary>
    public sealed class ChunkColumn
    {
        public readonly int X;
        public readonly int Z;
        public readonly Dictionary<int, ushort[]> Sections = new Dictionary<int, ushort[]>();
        public int MinSection = int.MaxValue;
        public int MaxSection = int.MinValue;

        public ChunkColumn(int x, int z)
        {
            X = x;
            Z = z;
        }

        public ushort Get(int lx, int y, int lz)
        {
            return Sections.TryGetValue(y >> 4, out var ids) ? ids[(lx << 8) | (lz << 4) | (y & 15)] : BlockRegistry.AirId;
        }
    }

    /// <summary>The decoded part of a world the viewer has loaded, for one dimension.</summary>
    public sealed class WorldData
    {
        /// <summary>The world file, or null for worlds built in code (shape gallery, tests).</summary>
        public readonly BedrockWorld? Source;
        public readonly Dimension Dimension;
        public readonly BlockRegistry Registry;
        public readonly ConcurrentDictionary<(int, int), ChunkColumn> Columns = new ConcurrentDictionary<(int, int), ChunkColumn>();

        public WorldData(BedrockWorld? source, Dimension dimension, BlockRegistry registry)
        {
            Source = source;
            Dimension = dimension;
            Registry = registry;
        }

        /// <summary>Places a block directly (for worlds built in code).</summary>
        public void SetBlock(int x, int y, int z, ushort id)
        {
            var column = Columns.GetOrAdd((x >> 4, z >> 4), k => new ChunkColumn(k.Item1, k.Item2));
            int section = y >> 4;
            if (!column.Sections.TryGetValue(section, out var ids))
            {
                column.Sections[section] = ids = new ushort[BlockStorage.Volume];
                column.MinSection = Math.Min(column.MinSection, section);
                column.MaxSection = Math.Max(column.MaxSection, section);
            }
            ids[((x & 15) << 8) | ((z & 15) << 4) | (y & 15)] = id;
        }

        public ChunkColumn? GetColumn(int cx, int cz) => Columns.TryGetValue((cx, cz), out var c) ? c : null;

        public ushort GetBlock(int x, int y, int z)
        {
            var column = GetColumn(x >> 4, z >> 4);
            return column?.Get(x & 15, y, z & 15) ?? BlockRegistry.AirId;
        }

        /// <summary>Decodes every stored sub-chunk of a column. Safe to call from worker threads.</summary>
        public ChunkColumn Decode(ChunkPos pos)
        {
            if (Source == null)
                throw new InvalidOperationException("This world has no source file to decode.");
            var column = new ChunkColumn(pos.X, pos.Z);
            var idsByPalette = new Dictionary<BlockState, ushort>();

            foreach (sbyte subY in Source.SubChunkIndices(pos))
            {
                SubChunk? sub;
                try
                {
                    sub = Source.GetSubChunk(pos.X, pos.Z, subY, pos.Dimension);
                }
                catch (System.NotSupportedException)
                {
                    continue; // legacy format: leave the section empty for now
                }
                if (sub == null || sub.Layers.Count == 0)
                    continue;

                var ids = new ushort[BlockStorage.Volume];
                var blocks = sub.Layers[0];
                var extra = sub.Layers.Count > 1 ? sub.Layers[1] : null;

                ushort[] blockMap = MapPalette(blocks, idsByPalette);
                ushort[]? extraMap = extra == null ? null : MapPalette(extra, idsByPalette);
                var visuals = Registry.Snapshot();

                bool any = false;
                for (int i = 0; i < BlockStorage.Volume; i++)
                {
                    ushort id = blockMap[blocks.Indices[i]];

                    // Waterlogging: layer 1 holds the water around kelp, seagrass, slabs… Show the water when the
                    // main block would not hide it anyway.
                    if (extraMap != null)
                    {
                        ushort extraId = extraMap[extra!.Indices[i]];
                        if (extraId != BlockRegistry.AirId && visuals[extraId].Shape == BlockShape.Liquid)
                        {
                            var shape = visuals[id].Shape;
                            if (shape == BlockShape.None || shape == BlockShape.Cross)
                                id = extraId;
                        }
                    }

                    ids[i] = id;
                    any |= id != BlockRegistry.AirId;
                }

                if (!any)
                    continue;

                column.Sections[subY] = ids;
                if (subY < column.MinSection) column.MinSection = subY;
                if (subY > column.MaxSection) column.MaxSection = subY;
            }

            Columns[(pos.X, pos.Z)] = column;
            return column;
        }

        private ushort[] MapPalette(BlockStorage storage, Dictionary<BlockState, ushort> cache)
        {
            var map = new ushort[storage.Palette.Count];
            for (int i = 0; i < map.Length; i++)
            {
                var state = storage.Palette[i];
                if (!cache.TryGetValue(state, out ushort id))
                    cache[state] = id = Registry.GetOrAdd(state);
                map[i] = id;
            }
            return map;
        }
    }
}
