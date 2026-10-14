using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BedrockForge.Bedrock;
using BedrockForge.Rendering.Blocks;
using BedrockForge.Rendering.World;
using BedrockForge.Resources;
using UnityEngine;
using UnityEngine.Rendering;

namespace BedrockForge.Rendering
{
    /// <summary>
    /// Opens a Bedrock world, decodes the chunks around the spawn and shows them as hidden, unsaved GameObjects
    /// in the open scene. Decoding and meshing run on worker threads; call <see cref="Tick"/> regularly (the editor
    /// update loop) to upload finished meshes. Not a MonoBehaviour, so it works in the editor without Play mode.
    /// </summary>
    public sealed class WorldView : IDisposable
    {
        public Dimension Dimension { get; private set; } = Dimension.Overworld;
        /// <summary>Radius in chunks around the spawn.</summary>
        public int ViewRadius { get; set; } = 16;
        /// <summary>Minecraft's data/resource_packs folder; empty = find the installed game.</summary>
        public string ResourcePacksPath { get; set; } = "";
        public bool UseTextures { get; set; } = true;
        public int UploadsPerTick { get; set; } = 8;

        public string? WorldFolder { get; private set; }
        public WorldData? Data { get; private set; }
        /// <summary>Edits of the open world file (null for the shape gallery).</summary>
        public WorldEditSession? Session { get; private set; }
        public TextureCatalog? Catalog { get; private set; }
        /// <summary>Where to look once loaded, in Minecraft coordinates (spawn surface or middle of the world).</summary>
        public Vector3 FocusPoint { get; private set; }
        public string Status { get; private set; } = "";
        public string TexturesInfo { get; private set; } = "";
        public int ChunksTotal { get; private set; }
        public int ChunksShown { get; private set; }
        public int BlocksShown { get; private set; }
        public bool IsLoading { get; private set; }

        /// <summary>Raised on the main thread once a world (or the gallery) is decoded and its textures built.</summary>
        public event Action? Loaded;

        private readonly ConcurrentQueue<ChunkMeshData> _ready = new ConcurrentQueue<ChunkMeshData>();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly Dictionary<(int, int), GameObject> _chunkObjects = new Dictionary<(int, int), GameObject>();
        private readonly Dictionary<(int, int), int> _chunkBlockCounts = new Dictionary<(int, int), int>();
        private CancellationTokenSource? _cts;
        private GameObject? _root;
        private Texture2DArray? _blockTextures;
        private int _builtTextureCount;
        private Material? _opaque, _cutout, _transparent;

        private const HideFlags Hidden = HideFlags.HideAndDontSave;

        /// <summary>Uploads meshes that finished building. Main thread only.</summary>
        public void Tick()
        {
            while (_mainThread.TryDequeue(out var action))
                action();
            for (int i = 0; i < UploadsPerTick && _ready.TryDequeue(out var mesh); i++)
                Upload(mesh);
        }

        public bool HasPendingUploads => !_ready.IsEmpty || !_mainThread.IsEmpty;

        /// <summary>
        /// Starts loading a world in the background. Results are applied by <see cref="Tick"/> on the main thread,
        /// so this does not depend on a synchronization context (works in batch mode too).
        /// </summary>
        public void Open(string folder, Dimension dimension)
        {
            // Clear() cancels the previous load: create the new token only afterwards.
            Clear();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Dimension = dimension;
            WorldFolder = folder;
            IsLoading = true;
            Status = $"Opening {Path.GetFileName(folder)}…";

            Task.Run(() =>
            {
                try
                {
                    var prepared = Prepare(folder, token);
                    var applied = new ManualResetEventSlim();
                    _mainThread.Enqueue(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ApplyPrepared(prepared);
                            Status = "Building chunks…";
                        }
                        applied.Set();
                    });
                    applied.Wait(token);

                    MeshAll(prepared, token, _ready.Enqueue);
                    _mainThread.Enqueue(() =>
                    {
                        Status = "";
                        IsLoading = false;
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _mainThread.Enqueue(() =>
                    {
                        Status = "Error: " + ex.Message;
                        IsLoading = false;
                        Debug.LogException(ex);
                    });
                }
            }, token);
        }

        public void Reload()
        {
            if (WorldFolder != null)
                Open(WorldFolder, Dimension);
        }

        /// <summary>Shows a hand-built scene with every supported shape instead of a world file.</summary>
        public void OpenGallery()
        {
            Clear();
            WorldFolder = null;
            var catalog = CreateCatalog();
            var data = new WorldData(null, Dimension.Overworld, new BlockRegistry(state => BlockModels.Create(state, catalog)));
            var center = ShapeGallery.Build(data);
            var prepared = new Prepared { Data = data, Columns = data.Columns.Values.ToList(), Center = center, Catalog = catalog };
            ApplyPrepared(prepared);
            FocusPoint = new Vector3(8f, 1f, 7f);
            MeshAll(prepared, CancellationToken.None, _ready.Enqueue);
        }

        private sealed class Prepared
        {
            public WorldData Data = null!;
            public List<ChunkColumn> Columns = null!;
            public ChunkPos Center;
            public TextureCatalog Catalog = null!;
            public WorldEditSession? Session;
        }

        private TextureCatalog CreateCatalog()
        {
            if (!UseTextures)
                return new TextureCatalog(null);

            string? dir = string.IsNullOrWhiteSpace(ResourcePacksPath) ? ResourcePackStack.FindInstalled() : ResourcePacksPath;
            if (dir == null || !Directory.Exists(dir))
            {
                Debug.LogWarning("[Bedrock Forge] Minecraft resource packs not found: using flat colors.");
                return new TextureCatalog(null);
            }

            try
            {
                return new TextureCatalog(ResourcePackStack.Load(dir));
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Bedrock Forge] Cannot read resource packs in {dir}: {ex.Message}. Using flat colors.");
                return new TextureCatalog(null);
            }
        }

        private Prepared Prepare(string folder, CancellationToken token)
        {
            var world = BedrockWorld.Open(folder);
            var catalog = CreateCatalog();
            var data = new WorldData(world, Dimension, new BlockRegistry(state => BlockModels.Create(state, catalog)));

            var positions = world.Chunks.Keys.Where(p => p.Dimension == Dimension).ToList();
            if (positions.Count == 0)
                throw new InvalidOperationException($"No chunks in dimension {Dimension}.");

            var center = PickCenter(world, positions);
            var selected = positions
                .Select(p => (pos: p, d: (p.X - center.X) * (p.X - center.X) + (p.Z - center.Z) * (p.Z - center.Z)))
                .Where(t => t.d <= ViewRadius * ViewRadius)
                .OrderBy(t => t.d)
                .Select(t => t.pos)
                .ToList();

            var columns = new ConcurrentBag<ChunkColumn>();
            Parallel.ForEach(selected, new ParallelOptions { CancellationToken = token }, pos => columns.Add(data.Decode(pos)));

            // Nearest first, so the area around the camera appears first.
            var ordered = columns
                .OrderBy(c => (c.X - center.X) * (c.X - center.X) + (c.Z - center.Z) * (c.Z - center.Z))
                .ToList();

            var session = new WorldEditSession(world);
            return new Prepared { Data = data, Columns = ordered, Center = center, Catalog = catalog, Session = session };
        }

        /// <summary>The spawn chunk if it exists, else the chunk closest to the middle of the explored area.</summary>
        private ChunkPos PickCenter(BedrockWorld world, List<ChunkPos> positions)
        {
            if (Dimension == Dimension.Overworld)
            {
                int? sx = world.LevelDat.GetInt("SpawnX");
                int? sz = world.LevelDat.GetInt("SpawnZ");
                if (sx.HasValue && sz.HasValue)
                {
                    var spawn = new ChunkPos(sx.Value >> 4, sz.Value >> 4, Dimension);
                    if (world.Chunks.ContainsKey(spawn))
                        return spawn;
                }
            }

            double mx = positions.Average(p => p.X);
            double mz = positions.Average(p => p.Z);
            return positions.OrderBy(p => (p.X - mx) * (p.X - mx) + (p.Z - mz) * (p.Z - mz)).First();
        }

        private static void MeshAll(Prepared prepared, CancellationToken token, Action<ChunkMeshData> onReady)
        {
            var visuals = prepared.Data.Registry.Snapshot();
            Parallel.ForEach(prepared.Columns, new ParallelOptions { CancellationToken = token },
                column => onReady(ChunkMesher.Build(prepared.Data, column, visuals)));
        }

        private void ApplyPrepared(Prepared prepared)
        {
            Data = prepared.Data;
            Session = prepared.Session;
            Catalog = prepared.Catalog;
            ChunksTotal = prepared.Columns.Count;

            int x = prepared.Center.X * 16 + 8, z = prepared.Center.Z * 16 + 8;
            FocusPoint = new Vector3(x, TopBlockY(prepared.Data, x, z) ?? 64, z);

            // Every block state is known once decoding is done: pack their textures for the shaders.
            RebuildTextures();
            Loaded?.Invoke();
        }

        private void RebuildTextures()
        {
            if (Catalog == null)
                return;
            DestroyAny(_blockTextures);
            _blockTextures = Catalog.Build();
            _blockTextures.hideFlags = Hidden;
            _builtTextureCount = Catalog.Count;
            Shader.SetGlobalTexture(TextureCatalog.GlobalTextureName, _blockTextures);
            TexturesInfo = Catalog.Packs == null
                ? "flat colors"
                : $"{Catalog.Count - 1} textures, {Catalog.Packs.Packs.Count} packs";
        }

        /// <summary>Changes one block in the rendered world and rebuilds the affected chunk meshes.</summary>
        public void SetRenderedBlock(int x, int y, int z, BlockState state)
        {
            if (Data == null)
                return;
            Data.SetBlock(x, y, z, state.IsAir ? BlockRegistry.AirId : Data.Registry.GetOrAdd(state));

            // A newly seen block state may need textures that are not in the array yet.
            if (Catalog != null && Catalog.Count != _builtTextureCount)
                RebuildTextures();

            int cx = x >> 4, cz = z >> 4, lx = x & 15, lz = z & 15;
            Remesh(cx, cz);
            if (lx == 0) Remesh(cx - 1, cz);
            if (lx == 15) Remesh(cx + 1, cz);
            if (lz == 0) Remesh(cx, cz - 1);
            if (lz == 15) Remesh(cx, cz + 1);
        }

        public void Remesh(int cx, int cz)
        {
            var column = Data?.GetColumn(cx, cz);
            if (Data == null || column == null)
                return;
            Upload(ChunkMesher.Build(Data, column, Data.Registry.Snapshot()));
        }

        public static int? TopBlockY(WorldData data, int x, int z)
        {
            var column = data.GetColumn(x >> 4, z >> 4);
            if (column == null || column.Sections.Count == 0)
                return null;

            var visuals = data.Registry.Snapshot();
            for (int y = column.MaxSection * 16 + 15; y >= column.MinSection * 16; y--)
            {
                ushort id = column.Get(x & 15, y, z & 15);
                if (id != BlockRegistry.AirId && id < visuals.Length && !visuals[id].IsAir)
                    return y;
            }
            return null;
        }

        private Transform EnsureRoot()
        {
            if (_root != null)
                return _root.transform;

            // Minecraft is right-handed, Unity left-handed: mirroring Z keeps north/south the right way round.
            // Unity flips the triangle winding automatically for negative scales.
            _root = new GameObject("Bedrock Forge World") { hideFlags = Hidden };
            _root.transform.localScale = new Vector3(1f, 1f, -1f);
            return _root.transform;
        }

        private Material MaterialFor(ref Material? cache, string shader)
        {
            if (cache == null)
            {
                var s = Shader.Find(shader) ?? throw new InvalidOperationException($"Shader {shader} not found.");
                cache = new Material(s) { hideFlags = Hidden, name = shader };
            }
            return cache;
        }

        private void Upload(ChunkMeshData data)
        {
            var root = EnsureRoot();
            if (_chunkObjects.TryGetValue((data.ChunkX, data.ChunkZ), out var old))
            {
                DestroyChunkObject(old);
                ChunksShown--;
                BlocksShown -= _chunkBlockCounts.TryGetValue((data.ChunkX, data.ChunkZ), out int previous) ? previous : 0;
            }

            var chunk = new GameObject($"Chunk {data.ChunkX},{data.ChunkZ}") { hideFlags = Hidden };
            chunk.transform.SetParent(root, false);
            chunk.transform.localPosition = new Vector3(data.ChunkX * 16, 0, data.ChunkZ * 16);

            AddPart(chunk.transform, "Opaque", data.Opaque, MaterialFor(ref _opaque, "BedrockForge/VertexColor"));
            AddPart(chunk.transform, "Cutout", data.Cutout, MaterialFor(ref _cutout, "BedrockForge/VertexColorCutout"));
            AddPart(chunk.transform, "Transparent", data.Transparent, MaterialFor(ref _transparent, "BedrockForge/VertexColorTransparent"));

            _chunkObjects[(data.ChunkX, data.ChunkZ)] = chunk;
            _chunkBlockCounts[(data.ChunkX, data.ChunkZ)] = data.BlockCount;
            ChunksShown++;
            BlocksShown += data.BlockCount;
        }

        private static void AddPart(Transform parent, string name, MeshBuffers buffers, Material material)
        {
            if (buffers.IsEmpty)
                return;

            var go = new GameObject(name) { hideFlags = Hidden };
            go.transform.SetParent(parent, false);
            var mesh = buffers.ToMesh(parent.name + " " + name);
            mesh.hideFlags = Hidden;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Clear()
        {
            _cts?.Cancel();
            while (_ready.TryDequeue(out _)) { }
            while (_mainThread.TryDequeue(out _)) { }
            IsLoading = false;
            foreach (var go in _chunkObjects.Values)
                DestroyChunkObject(go);
            _chunkObjects.Clear();
            _chunkBlockCounts.Clear();
            DestroyAny(_root);
            _root = null;
            ChunksShown = 0;
            ChunksTotal = 0;
            BlocksShown = 0;
            Data = null;
            Session = null;
            DestroyAny(_blockTextures);
            _blockTextures = null;
            Status = "";
        }

        public void Dispose()
        {
            Clear();
            DestroyAny(_opaque);
            DestroyAny(_cutout);
            DestroyAny(_transparent);
        }

        private static void DestroyChunkObject(GameObject? go)
        {
            if (go == null) return;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                DestroyAny(filter.sharedMesh);
            DestroyAny(go);
        }

        private static void DestroyAny(UnityEngine.Object? o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
