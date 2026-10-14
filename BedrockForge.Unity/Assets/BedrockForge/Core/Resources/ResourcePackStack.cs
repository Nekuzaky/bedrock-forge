using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BedrockForge.Json;

namespace BedrockForge.Resources
{
    /// <summary>A texture reference from <c>terrain_texture.json</c>: a path without extension, plus an optional tint.</summary>
    public sealed class TextureRef
    {
        public string Path { get; }
        /// <summary>Hex color from <c>overlay_color</c> (only where the texture's alpha marks it) or <c>tint_color</c>.</summary>
        public string? OverlayColor { get; }
        public string? TintColor { get; }

        public TextureRef(string path, string? overlayColor, string? tintColor)
        {
            Path = path;
            OverlayColor = overlayColor;
            TintColor = tintColor;
        }

        public string CacheKey => $"{Path}|{OverlayColor}|{TintColor}";
    }

    /// <summary>
    /// The game's built-in resource packs stacked like the game does: <c>vanilla</c>, then every <c>vanilla_x.y.z</c>
    /// in version order, later packs overriding earlier ones. Reads loose files and <c>__brarchive</c> bundles.
    /// </summary>
    public sealed class ResourcePackStack
    {
        /// <summary>Face order used everywhere in Bedrock Forge: +Y, -Y, +X (east), -X (west), +Z (south), -Z (north).</summary>
        public static readonly string[] FaceNames = { "up", "down", "east", "west", "south", "north" };

        private readonly Dictionary<string, Func<byte[]>> _files = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, object?>> _blocks = new Dictionary<string, Dictionary<string, object?>>();
        private readonly Dictionary<string, Dictionary<string, object?>> _terrain = new Dictionary<string, Dictionary<string, object?>>();

        public IReadOnlyList<string> Packs { get; }
        public int FileCount => _files.Count;
        public int BlockDefinitionCount => _blocks.Count;
        public int TerrainTextureCount => _terrain.Count;

        private ResourcePackStack(List<string> packs)
        {
            Packs = packs;
        }

        /// <summary>Known install locations of the Windows (GDK) version.</summary>
        public static string? FindInstalled()
        {
            var candidates = new List<string>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                candidates.Add(System.IO.Path.Combine(drive.RootDirectory.FullName, "XboxGames", "Minecraft for Windows", "Content", "data", "resource_packs"));
            return candidates.FirstOrDefault(Directory.Exists);
        }

        public static ResourcePackStack Load(string resourcePacksDir)
        {
            var packs = Directory.GetDirectories(resourcePacksDir)
                .Select(d => (dir: d, version: VersionOf(System.IO.Path.GetFileName(d))))
                .Where(t => t.version != null)
                .OrderBy(t => t.version!, VersionComparer.Instance)
                .Select(t => t.dir)
                .ToList();

            if (packs.Count == 0)
                throw new DirectoryNotFoundException($"No vanilla resource packs in {resourcePacksDir}");

            var stack = new ResourcePackStack(packs);
            foreach (var pack in packs)
                stack.AddPack(pack);
            return stack;
        }

        /// <summary>"vanilla" → [0]; "vanilla_1.21.50" → [1,21,50]; anything else (vanilla_base, vanilla_music…) → null.</summary>
        private static int[]? VersionOf(string name)
        {
            if (name == "vanilla")
                return new[] { 0 };
            if (!name.StartsWith("vanilla_", StringComparison.Ordinal))
                return null;
            var parts = name.Substring(8).Split('.');
            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out numbers[i]))
                    return null;
            return numbers;
        }

        private void AddPack(string dir)
        {
            string archiveRoot = System.IO.Path.Combine(dir, "__brarchive");

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (file.StartsWith(archiveRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                string rel = Normalize(file.Substring(dir.Length + 1));
                if (!IsWanted(rel))
                    continue;
                string captured = file;
                _files[rel] = () => File.ReadAllBytes(captured);
            }

            if (Directory.Exists(archiveRoot))
            {
                foreach (var archivePath in Directory.EnumerateFiles(archiveRoot, "*.brarchive", SearchOption.AllDirectories))
                {
                    // __brarchive/textures/blocks.brarchive holds the files of textures/blocks/.
                    string folder = Normalize(archivePath.Substring(archiveRoot.Length + 1));
                    folder = folder.Substring(0, folder.Length - ".brarchive".Length);
                    if (!IsWanted(folder + "/"))
                        continue;

                    BrArchive archive;
                    try { archive = BrArchive.Open(archivePath); }
                    catch (Exception ex) when (ex is FormatException || ex is IOException) { continue; }

                    foreach (var name in archive.Entries.Keys)
                    {
                        string entry = name;
                        _files[folder + "/" + Normalize(name)] = () => archive.Read(entry);
                    }
                }
            }

            MergeJson("blocks.json", json => json, _blocks);
            MergeJson("textures/terrain_texture.json", json => json.TryGetValue("texture_data", out var td) ? td as Dictionary<string, object?> : null, _terrain);
        }

        /// <summary>Re-reads the stack's current version of a JSON file and merges its top-level objects.</summary>
        private void MergeJson(string path, Func<Dictionary<string, object?>, Dictionary<string, object?>?> select,
            Dictionary<string, Dictionary<string, object?>> target)
        {
            if (!_files.TryGetValue(path, out var read))
                return;
            Dictionary<string, object?>? root;
            try
            {
                root = JsonReader.ParseObject(System.Text.Encoding.UTF8.GetString(read()));
            }
            catch (FormatException)
            {
                return;
            }
            var section = root == null ? null : select(root);
            if (section == null)
                return;
            foreach (var kv in section)
                if (kv.Value is Dictionary<string, object?> obj)
                    target[kv.Key] = obj;
            // Each pack is merged right after being added; drop the entry so the next pack's copy is read fresh.
            _files.Remove(path);
        }

        private static string Normalize(string path) => path.Replace('\\', '/');

        /// <summary>Only what block rendering needs: block/terrain definitions, block textures and colormaps.</summary>
        private static bool IsWanted(string path) =>
            path.Equals("blocks.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("textures/terrain_texture.json", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("textures/blocks/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("textures/colormap/", StringComparison.OrdinalIgnoreCase);

        public bool Exists(string path) => _files.ContainsKey(Normalize(path));

        public byte[]? ReadFile(string path) => _files.TryGetValue(Normalize(path), out var read) ? read() : null;

        /// <summary>Finds the image file for a path without extension (packs use .png and .tga).</summary>
        public string? FindImage(string pathWithoutExtension)
        {
            foreach (var ext in new[] { ".png", ".tga" })
                if (_files.ContainsKey(pathWithoutExtension + ext))
                    return pathWithoutExtension + ext;
            return null;
        }

        public bool HasBlock(string blockName) => _blocks.ContainsKey(blockName);

        /// <summary>Every block name the packs define (without namespace), e.g. for a block picker.</summary>
        public IEnumerable<string> BlockNames => _blocks.Keys.Where(k => k != "format_version");

        /// <summary>
        /// Terrain texture keys for the six faces (see <see cref="FaceNames"/>) from <c>blocks.json</c>,
        /// or null when the block has no texture entry.
        /// </summary>
        public string?[]? GetBlockFaceKeys(string blockName)
        {
            if (!_blocks.TryGetValue(blockName, out var def) || !def.TryGetValue("textures", out var textures) || textures == null)
                return null;

            var faces = new string?[6];
            if (textures is string all)
            {
                for (int i = 0; i < 6; i++) faces[i] = all;
                return faces;
            }
            if (textures is Dictionary<string, object?> map)
            {
                string? side = map.TryGetValue("side", out var s) ? s as string : null;
                for (int i = 0; i < 6; i++)
                {
                    faces[i] = map.TryGetValue(FaceNames[i], out var v) && v is string str ? str : i >= 2 ? side : null;
                }
                // Some blocks only give "side" and "up": use "up" for the bottom too.
                faces[1] ??= faces[0];
                return faces;
            }
            return null;
        }

        public bool HasTerrainTexture(string key) => _terrain.ContainsKey(key);

        /// <summary>Resolves a terrain texture key. <paramref name="variant"/> picks among the arrays of legacy variants.</summary>
        public TextureRef? ResolveTerrain(string key, int variant = 0)
        {
            if (!_terrain.TryGetValue(key, out var entry) || !entry.TryGetValue("textures", out var textures))
                return null;

            object? chosen = textures;
            if (textures is List<object?> list && list.Count > 0)
                chosen = list[variant >= 0 && variant < list.Count ? variant : 0];

            switch (chosen)
            {
                case string path:
                    return new TextureRef(path, null, null);
                case Dictionary<string, object?> obj when obj.TryGetValue("path", out var p) && p is string path:
                    return new TextureRef(path,
                        obj.TryGetValue("overlay_color", out var oc) ? oc as string : null,
                        obj.TryGetValue("tint_color", out var tc) ? tc as string : null);
                default:
                    return null;
            }
        }

        private sealed class VersionComparer : IComparer<int[]>
        {
            public static readonly VersionComparer Instance = new VersionComparer();

            public int Compare(int[]? a, int[]? b)
            {
                a ??= Array.Empty<int>();
                b ??= Array.Empty<int>();
                for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
                {
                    int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                    if (x != y) return x.CompareTo(y);
                }
                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
