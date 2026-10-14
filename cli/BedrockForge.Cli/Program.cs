using BedrockForge.Resources;
using System.Diagnostics;
using BedrockForge.Bedrock;
using BedrockForge.Nbt;

const string Usage = """
    Bedrock Forge — Bedrock world inspector

    Usage:
      bedrockforge info     <world>
      bedrockforge chunks   <world> [dim] [--limit N]
      bedrockforge subchunk <world> <chunkX> <chunkZ> <subY> [dim]
      bedrockforge block    <world> <x> <y> <z> [dim]
      bedrockforge leveldat <world>
      bedrockforge keys     <world> [--limit N]

    <world>  world folder (the one containing level.dat and db/) or a .mcworld file
    [dim]    overworld (default), nether, end
    """;

if (args.Length < 2)
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 0 : 1;
}

try
{
    var sw = Stopwatch.StartNew();
    var world = BedrockWorld.Open(args[1]);
    var loadTime = sw.Elapsed;
    var rest = args.Skip(2).ToArray();

    switch (args[0].ToLowerInvariant())
    {
        case "info": Info(world, loadTime); break;
        case "chunks": ListChunks(world, rest); break;
        case "subchunk": ShowSubChunk(world, rest); break;
        case "block": ShowBlock(world, rest); break;
        case "leveldat": Console.WriteLine(NbtFormatter.ToSnbt(world.LevelDat)); break;
        case "keys": ListOtherKeys(world, rest); break;
        case "palette": ListBlockNames(world, rest); break;
        case "textures": CheckTextures(world, rest); break;
        case "dump": DumpKey(world, rest); break;
        case "edit-selftest": return EditSelfTest(args[1], rest);
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.\n");
            Console.WriteLine(Usage);
            return 1;
    }
    return 0;
}
catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}

static void Info(BedrockWorld world, TimeSpan loadTime)
{
    var lastVersion = world.LevelDat.Get<NbtList>("lastOpenedWithVersion");
    Console.WriteLine($"World      : {world.Name}");
    Console.WriteLine($"Folder     : {world.Folder}");
    Console.WriteLine($"Game ver.  : {(lastVersion == null ? "?" : string.Join(".", lastVersion))}");
    Console.WriteLine($"Storage    : level.dat v{world.LevelDatStorageVersion}");
    Console.WriteLine($"Seed       : {(world.LevelDat.TryGetValue("RandomSeed", out var seed) ? seed : "?")}");
    Console.WriteLine($"Spawn      : {world.LevelDat.GetInt("SpawnX")}, {world.LevelDat.GetInt("SpawnY")}, {world.LevelDat.GetInt("SpawnZ")}");
    Console.WriteLine($"DB keys    : {world.Db.Count:N0} ({world.OtherKeys.Count:N0} non-chunk)");
    Console.WriteLine($"Loaded in  : {loadTime.TotalMilliseconds:N0} ms");
    Console.WriteLine();

    foreach (var group in world.Chunks.Keys.GroupBy(p => p.Dimension).OrderBy(g => g.Key))
    {
        int subChunks = group.Sum(p => world.SubChunkIndices(p).Count());
        Console.WriteLine($"{group.Key,-10} : {group.Count():N0} chunks, {subChunks:N0} sub-chunks, " +
                          $"X {group.Min(p => p.X)}..{group.Max(p => p.X)}, Z {group.Min(p => p.Z)}..{group.Max(p => p.Z)}");
    }
}

static void ListChunks(BedrockWorld world, string[] rest)
{
    var dim = ParseDim(rest.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _)));
    int limit = ParseLimit(rest, 50);

    var chunks = world.Chunks.Keys.Where(p => p.Dimension == dim).OrderBy(p => p.X).ThenBy(p => p.Z).ToList();
    Console.WriteLine($"{chunks.Count:N0} chunks in {dim} (showing {Math.Min(limit, chunks.Count)})");
    foreach (var pos in chunks.Take(limit))
    {
        var subs = world.SubChunkIndices(pos).ToList();
        string range = subs.Count == 0 ? "none" : $"{subs.First()}..{subs.Last()} ({subs.Count})";
        Console.WriteLine($"  chunk ({pos.X,5}, {pos.Z,5})  sub-chunks {range}");
    }
}

static void ShowSubChunk(BedrockWorld world, string[] rest)
{
    if (rest.Length < 3)
        throw new ArgumentException("subchunk needs <chunkX> <chunkZ> <subY>.");
    int cx = int.Parse(rest[0]), cz = int.Parse(rest[1]), sy = int.Parse(rest[2]);
    var dim = ParseDim(rest.ElementAtOrDefault(3));

    var sub = world.GetSubChunk(cx, cz, sy, dim);
    if (sub == null)
    {
        Console.WriteLine($"No sub-chunk at {dim} chunk ({cx}, {cz}) y-index {sy} — it is empty (air) or not generated.");
        return;
    }

    Console.WriteLine($"Sub-chunk {dim} chunk ({cx}, {cz}) y-index {sy}  →  blocks Y {sy * 16}..{sy * 16 + 15}");
    Console.WriteLine($"Format v{sub.Format}, {sub.Layers.Count} layer(s)");

    for (int l = 0; l < sub.Layers.Count; l++)
    {
        var layer = sub.Layers[l];
        var counts = new int[layer.Palette.Count];
        foreach (var idx in layer.Indices) counts[idx]++;

        Console.WriteLine();
        Console.WriteLine($"Layer {l}: {layer.BitsPerBlock} bits/block, palette of {layer.Palette.Count}");
        foreach (var (state, count) in layer.Palette.Zip(counts).OrderByDescending(t => t.Second))
            Console.WriteLine($"  {count,5}  {state}");
    }
}

static void ShowBlock(BedrockWorld world, string[] rest)
{
    if (rest.Length < 3)
        throw new ArgumentException("block needs <x> <y> <z>.");
    int x = int.Parse(rest[0]), y = int.Parse(rest[1]), z = int.Parse(rest[2]);
    var dim = ParseDim(rest.ElementAtOrDefault(3));

    var block = world.GetBlock(x, y, z, dim);
    var extra = world.GetBlock(x, y, z, dim, layer: 1);
    Console.WriteLine($"{dim} ({x}, {y}, {z})  chunk ({x >> 4}, {z >> 4}) sub-chunk {y >> 4}");
    Console.WriteLine($"  block : {block?.ToString() ?? "minecraft:air (no sub-chunk stored)"}");
    if (extra != null && !extra.IsAir)
        Console.WriteLine($"  layer1: {extra}");
}

static void ListOtherKeys(BedrockWorld world, string[] rest)
{
    int limit = ParseLimit(rest, 100);
    var keys = world.OtherKeys.Select(BedrockWorld.DescribeKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
    Console.WriteLine($"{keys.Count:N0} non-chunk keys (showing {Math.Min(limit, keys.Count)})");
    foreach (var k in keys.Take(limit))
        Console.WriteLine($"  {k}");
}

static void ListBlockNames(BedrockWorld world, string[] rest)
{
    var dim = ParseDim(rest.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _)));
    int limit = ParseLimit(rest, 500);
    var counts = new Dictionary<string, long>();

    foreach (var pos in world.Chunks.Keys.Where(p => p.Dimension == dim))
        foreach (var y in world.SubChunkIndices(pos))
        {
            SubChunk? sub;
            try { sub = world.GetSubChunk(pos.X, pos.Z, y, dim); }
            catch (NotSupportedException) { continue; }
            if (sub == null) continue;
            foreach (var layer in sub.Layers)
                foreach (var idx in layer.Indices)
                {
                    string name = layer.Palette[idx].Name;
                    counts[name] = counts.GetValueOrDefault(name) + 1;
                }
        }

    Console.WriteLine($"{counts.Count} block types in {dim}");
    foreach (var (name, count) in counts.OrderByDescending(kv => kv.Value).Take(limit))
        Console.WriteLine($"  {count,12:N0}  {name}");
}

static void CheckTextures(BedrockWorld world, string[] rest)
{
    string? packsDir = rest.FirstOrDefault(a => !a.StartsWith("--")) ?? ResourcePackStack.FindInstalled();
    if (packsDir == null)
        throw new ArgumentException("Minecraft's resource_packs folder was not found; pass it as an argument.");

    var sw = Stopwatch.StartNew();
    var packs = ResourcePackStack.Load(packsDir);
    Console.WriteLine($"{packs.Packs.Count} packs, {packs.FileCount:N0} files, {packs.BlockDefinitionCount} block definitions, " +
                      $"{packs.TerrainTextureCount} terrain textures ({sw.ElapsedMilliseconds} ms)");

    var names = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var pos in world.Chunks.Keys)
        foreach (var y in world.SubChunkIndices(pos))
        {
            SubChunk? sub;
            try { sub = world.GetSubChunk(pos.X, pos.Z, y, pos.Dimension); }
            catch (NotSupportedException) { continue; }
            if (sub == null) continue;
            foreach (var layer in sub.Layers)
                foreach (var state in layer.Palette)
                    names.Add(state.Name.Replace("minecraft:", ""));
        }

    int ok = 0;
    foreach (var name in names)
    {
        var keys = packs.GetBlockFaceKeys(name);
        if (keys == null)
        {
            Console.WriteLine($"  [no blocks.json]  {name}{(packs.HasTerrainTexture(name) ? "  (terrain key exists)" : "")}");
            continue;
        }
        var missing = keys.Where(k => k != null).Distinct()
            .Where(k => packs.ResolveTerrain(k!) is not { } t || packs.FindImage(t.Path) == null).ToList();
        if (missing.Count > 0)
            Console.WriteLine($"  [missing image]   {name}: {string.Join(", ", missing)}");
        else
            ok++;
    }
    Console.WriteLine($"{ok}/{names.Count} block types fully textured.");
    if (rest.Contains("--verbose"))
        foreach (var name in names)
            if (packs.GetBlockFaceKeys(name) is { } keys)
                Console.WriteLine($"  {name}: {string.Join(" | ", keys.Select(k => k == null ? "-" : $"{k} → {packs.ResolveTerrain(k)?.Path}"))}");
}

/// <summary>Prints a raw DB value as NBT. Key: text ("~local_player"), "0x" + hex, or "prefix:" to dump the first key starting with prefix.</summary>
static void DumpKey(BedrockWorld world, string[] rest)
{
    if (rest.Length < 1)
        throw new ArgumentException("dump needs a key.");
    string arg = rest[0];
    byte[]? key = null;
    if (arg.StartsWith("0x"))
        key = Convert.FromHexString(arg.Substring(2));
    else if (arg.EndsWith(":"))
    {
        var prefix = System.Text.Encoding.ASCII.GetBytes(arg.TrimEnd(':'));
        key = world.Db.Keys.FirstOrDefault(k => k.AsSpan().StartsWith(prefix));
    }
    else
        key = System.Text.Encoding.ASCII.GetBytes(arg);

    if (key == null || !world.Db.TryGet(key, out var value))
    {
        Console.WriteLine("Key not found.");
        return;
    }

    Console.WriteLine($"Key {BedrockWorld.DescribeKey(key)}: {value.Length} bytes");
    var r = new BedrockForge.IO.ByteReader(value);
    int n = 0;
    while (!r.Eof && n < 20)
    {
        try
        {
            var (name, tag) = NbtReader.ReadRoot(r);
            Console.WriteLine($"--- root #{n++} '{name}'");
            Console.WriteLine(NbtFormatter.ToSnbt(tag));
        }
        catch (FormatException)
        {
            Console.WriteLine($"(not NBT) {BitConverter.ToString(value, 0, Math.Min(value.Length, 64))}");
            break;
        }
    }
}

/// <summary>
/// Copies a world, edits level.dat, a block, the player and an entity, saves, reopens and checks every change.
/// Usage: edit-selftest &lt;sourceWorld&gt; &lt;scratchFolder&gt;
/// </summary>
static int EditSelfTest(string source, string[] rest)
{
    string scratch = rest.FirstOrDefault() ?? throw new ArgumentException("edit-selftest needs a scratch folder.");
    if (Directory.Exists(scratch))
        Directory.Delete(scratch, true);
    CopyDirectory(source, scratch);

    var world = BedrockWorld.Open(scratch);
    var session = new WorldEditSession(world);
    Console.WriteLine($"Block version {session.BlockVersion}, last sequence {world.Db.LastSequence}");

    // level.dat: switch to creative.
    int oldMode = session.LevelDat.GetInt("GameType") ?? -1;
    session.LevelDat["GameType"] = oldMode == 1 ? 0 : 1;
    session.MarkLevelDatChanged();

    // A block, inside a generated chunk.
    var chunk = world.Chunks.Keys.First(p => p.Dimension == Dimension.Overworld && world.SubChunkIndices(p).Any());
    int bx = chunk.X * 16 + 5, bz = chunk.Z * 16 + 7, by = 100;
    var diamond = session.CreateState("diamond_block");
    session.SetBlock(bx, by, bz, diamond);
    var below = session.GetBlock(bx, -60, bz);
    session.SetBlock(bx, -60, bz, session.CreateState("gold_block"));

    // Player position.
    var player = session.Players().FirstOrDefault();
    if (player != null)
    {
        var pos = player.Nbt.Get<NbtList>("Pos")!;
        pos[1] = (float)pos[1] + 10f;
        session.SavePlayer(player);
    }

    // First entity: move 40 blocks east (likely another chunk) and rename it.
    var entity = session.Entities().FirstOrDefault(e => e.Position != null);
    string? entityId = null;
    if (entity != null)
    {
        var pos = entity.Nbt.Get<NbtList>("Pos")!;
        pos[0] = (float)pos[0] + 40f;
        entity.Nbt["CustomName"] = "Bedrock Forge";
        session.SaveEntity(entity);
        entityId = entity.Nbt["UniqueID"].ToString();
    }

    Console.WriteLine($"Pending changes: {session.PendingChanges}");
    var result = session.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scratch))!, "backups"));
    Console.WriteLine($"Saved {result.RecordsWritten} records, level.dat {result.LevelDatWritten}, log {Path.GetFileName(result.LogPath)}, backup {Path.GetFileName(result.BackupPath)}");

    // Reopen from disk and check.
    var reopened = BedrockWorld.Open(scratch);
    var check = new WorldEditSession(reopened);
    int failures = 0;
    void Expect(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what}");
        if (!ok) failures++;
    }

    Expect(check.LevelDat.GetInt("GameType") == (oldMode == 1 ? 0 : 1), "level.dat GameType");
    Expect(check.GetBlock(bx, by, bz).Name == "minecraft:diamond_block", $"diamond block at {bx},{by},{bz}");
    Expect(check.GetBlock(bx, -60, bz).Name == "minecraft:gold_block", $"gold block at {bx},-60,{bz} (was {below.Name})");
    Expect(check.GetBlock(bx + 1, -60, bz).Equals(session.GetBlock(bx + 1, -60, bz)), "neighbour block unchanged");
    if (player != null)
        Expect(Math.Abs((float)check.Players()[0].Nbt.Get<NbtList>("Pos")![1] - (float)player.Nbt.Get<NbtList>("Pos")![1]) < 0.01f, "player Y");
    if (entityId != null)
    {
        var moved = check.Entities().FirstOrDefault(e => e.Nbt["UniqueID"].ToString() == entityId);
        Expect(moved != null && moved.Nbt.GetString("CustomName") == "Bedrock Forge", "entity renamed and still indexed");
        Expect(moved != null && moved.Chunk.X == (int)Math.Floor(moved.Position!.Value.x) >> 4, "entity indexed in its new chunk");
        Expect(check.Entities().Count(e => e.Nbt["UniqueID"].ToString() == entityId) == 1, "entity listed once");
    }
    Expect(reopened.Db.Count >= world.Db.Count, "no records lost");

    Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
    return failures == 0 ? 0 : 3;
}

static void CopyDirectory(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.GetFiles(from))
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    foreach (var dir in Directory.GetDirectories(from))
        CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
}

static Dimension ParseDim(string? s) => s?.ToLowerInvariant() switch
{
    null or "overworld" or "0" => Dimension.Overworld,
    "nether" or "1" => Dimension.Nether,
    "end" or "2" => Dimension.End,
    _ => throw new ArgumentException($"Unknown dimension '{s}'."),
};

static int ParseLimit(string[] rest, int fallback)
{
    int i = Array.IndexOf(rest, "--limit");
    return i >= 0 && i + 1 < rest.Length && int.TryParse(rest[i + 1], out int n) ? n : fallback;
}
