using System;
using System.Collections.Generic;
using System.Linq;
using BedrockForge.Bedrock;

namespace BedrockForge.Rendering.Blocks
{
    /// <summary>
    /// Assigns a compact global id to every distinct block state seen in the world, so meshing works on
    /// plain <c>ushort</c> arrays. Id 0 is always air. Thread-safe: chunks are decoded on worker threads.
    /// </summary>
    public sealed class BlockRegistry
    {
        private readonly object _lock = new object();
        private readonly Func<BlockState, BlockVisual> _factory;
        private readonly Dictionary<string, ushort> _ids = new Dictionary<string, ushort>();
        private readonly List<BlockVisual> _visuals = new List<BlockVisual> { BlockVisual.CreateAir() };
        private readonly List<string> _names = new List<string> { "minecraft:air" };
        private readonly List<BlockState?> _states = new List<BlockState?> { null };
        private BlockVisual[] _snapshot;

        public const ushort AirId = 0;

        public BlockRegistry(Func<BlockState, BlockVisual> factory)
        {
            _factory = factory;
            _snapshot = _visuals.ToArray();
        }

        public int Count
        {
            get { lock (_lock) return _visuals.Count; }
        }

        public ushort GetOrAdd(BlockState state)
        {
            if (state.IsAir)
                return AirId;

            string key = state.ToString();
            lock (_lock)
            {
                if (_ids.TryGetValue(key, out ushort id))
                    return id;

                id = (ushort)_visuals.Count;
                _ids[key] = id;
                _visuals.Add(_factory(state));
                _names.Add(key);
                _states.Add(state);
                _snapshot = _visuals.ToArray();
                return id;
            }
        }

        /// <summary>Lock-free view for the mesher. Taken after all chunks of a batch are decoded.</summary>
        public BlockVisual[] Snapshot()
        {
            lock (_lock) return _snapshot;
        }

        /// <summary>Every block state seen so far (without air), e.g. for a block picker.</summary>
        public List<BlockState> AllStates()
        {
            lock (_lock) return _states.Where(s => s != null).Select(s => s!).ToList();
        }

        public string NameOf(ushort id)
        {
            lock (_lock) return id < _names.Count ? _names[id] : "?";
        }
    }
}
