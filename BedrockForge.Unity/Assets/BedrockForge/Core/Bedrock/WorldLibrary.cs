using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BedrockForge.IO;
using BedrockForge.LevelDb;
using BedrockForge.Nbt;

namespace BedrockForge.Bedrock
{
    /// <summary>What the world list shows, read without opening the database.</summary>
    public sealed class WorldSummary
    {
        public string Folder { get; internal set; } = "";
        public string FolderName => Path.GetFileName(Folder);
        public string Name { get; internal set; } = "";
        public string? IconPath { get; internal set; }
        public DateTime? LastPlayed { get; internal set; }
        public int? GameType { get; internal set; }
        public long SizeBytes { get; internal set; }
        public bool IsEncrypted { get; internal set; }
        /// <summary>Where the world was found, e.g. "Minecraft (profile 2943…)".</summary>
        public string Source { get; internal set; } = "";
    }

    /// <summary>Finds the worlds of the installed game and makes safe copies of them.</summary>
    public static class WorldLibrary
    {
        /// <summary>The <c>minecraftWorlds</c> folders of every install and user profile on this machine.</summary>
        public static List<(string path, string label)> FindWorldRoots()
        {
            var roots = new List<(string, string)>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Windows (GDK) versions: one folder per signed-in user, plus Shared.
            foreach (var (product, label) in new[] { ("Minecraft Bedrock", "Minecraft"), ("Minecraft Bedrock Preview", "Minecraft Preview") })
            {
                string users = Path.Combine(appData, product, "Users");
                if (!Directory.Exists(users)) continue;
                foreach (var user in Directory.GetDirectories(users))
                {
                    string worlds = Path.Combine(user, "games", "com.mojang", "minecraftWorlds");
                    if (Directory.Exists(worlds))
                    {
                        string who = Path.GetFileName(user);
                        roots.Add((worlds, who == "Shared" ? $"{label} (shared)" : $"{label} (profile {Shorten(who)})"));
                    }
                }
            }

            // Older Microsoft Store (UWP) versions.
            foreach (var (package, label) in new[]
                     {
                         ("Microsoft.MinecraftUWP_8wekyb3d8bbwe", "Minecraft (legacy version)"),
                         ("Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "Minecraft Preview (legacy version)"),
                     })
            {
                string worlds = Path.Combine(localAppData, "Packages", package, "LocalState", "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(worlds))
                    roots.Add((worlds, label));
            }

            return roots;
        }

        public static List<WorldSummary> FindWorlds()
        {
            var worlds = new List<WorldSummary>();
            foreach (var (root, label) in FindWorldRoots())
                foreach (var folder in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(folder, "level.dat")))
                    {
                        var summary = Describe(folder);
                        summary.Source = label;
                        worlds.Add(summary);
                    }

            return worlds.OrderByDescending(w => w.LastPlayed ?? DateTime.MinValue).ToList();
        }

        public static WorldSummary Describe(string folder)
        {
            var summary = new WorldSummary { Folder = folder, Name = Path.GetFileName(folder) };

            string namePath = Path.Combine(folder, "levelname.txt");
            if (File.Exists(namePath))
                summary.Name = File.ReadAllText(namePath, Encoding.UTF8).Trim();

            foreach (var icon in new[] { "world_icon.jpeg", "world_icon.png" })
                if (File.Exists(Path.Combine(folder, icon)))
                    summary.IconPath = Path.Combine(folder, icon);

            try
            {
                var bytes = File.ReadAllBytes(Path.Combine(folder, "level.dat"));
                var r = new ByteReader(bytes, 8);
                var dat = NbtReader.ReadRootCompound(r);
                if (dat.GetString("LevelName") is { Length: > 0 } levelName && !File.Exists(namePath))
                    summary.Name = levelName;
                summary.GameType = dat.GetInt("GameType");
                if (dat.TryGetValue("LastPlayed", out var lp) && lp is long seconds && seconds > 0)
                    summary.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
            {
            }

            try
            {
                summary.SizeBytes = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                summary.IsEncrypted = LevelDbReader.IsEncryptedDatabase(Path.Combine(folder, "db"));
            }
            catch (IOException)
            {
            }

            return summary;
        }

        /// <summary>
        /// Copies a world next to the original, under a new name the game will list, e.g. "My world (copy)".
        /// Returns the new folder.
        /// </summary>
        public static string CopyWorld(string folder, string nameSuffix = " (Bedrock Forge copy)")
        {
            string parent = Path.GetDirectoryName(folder)!;
            string target = UniqueFolder(parent, Path.GetFileName(folder) + "-bf");
            CopyDirectory(folder, target);

            // The game's own lock file must not travel with the copy.
            string lockFile = Path.Combine(target, "db", "LOCK");
            if (File.Exists(lockFile))
                File.Delete(lockFile);

            Rename(target, Describe(folder).Name + nameSuffix);
            return target;
        }

        /// <summary>Extracts a .mcworld archive into a world folder of <paramref name="worldsRoot"/>.</summary>
        public static string ImportMcWorld(string archive, string worldsRoot)
        {
            string target = UniqueFolder(worldsRoot, "bf-" + Path.GetFileNameWithoutExtension(archive).Replace(' ', '_'));
            ZipFile.ExtractToDirectory(archive, target);

            // Some archives wrap the world in one extra folder.
            if (!File.Exists(Path.Combine(target, "level.dat")))
            {
                var inner = Directory.GetDirectories(target).FirstOrDefault(d => File.Exists(Path.Combine(d, "level.dat")));
                if (inner == null)
                    throw new InvalidDataException("This file does not contain a world (no level.dat).");
                foreach (var entry in Directory.GetFileSystemEntries(inner))
                {
                    string dest = Path.Combine(target, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest);
                }
                Directory.Delete(inner);
            }
            return target;
        }

        /// <summary>Changes the name shown in the game's world list (level.dat and levelname.txt).</summary>
        public static void Rename(string folder, string name)
        {
            // Only level.dat changes: no need to load the whole database.
            string path = Path.Combine(folder, "level.dat");
            var bytes = File.ReadAllBytes(path);
            var r = new ByteReader(bytes);
            int storageVersion = r.S32();
            r.S32();
            var dat = NbtReader.ReadRootCompound(r);
            dat["LevelName"] = name;

            var nbt = NbtWriter.ToBytes(dat);
            var w = new ByteWriter();
            w.S32(storageVersion);
            w.S32(nbt.Length);
            w.Bytes(nbt);
            File.WriteAllBytes(path, w.ToArray());
            File.WriteAllText(Path.Combine(folder, "levelname.txt"), name, new UTF8Encoding(false));
        }

        private static string UniqueFolder(string parent, string baseName)
        {
            string path = Path.Combine(parent, baseName);
            for (int i = 2; Directory.Exists(path); i++)
                path = Path.Combine(parent, $"{baseName}-{i}");
            return path;
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(from))
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private static string Shorten(string id) => id.Length > 8 ? id.Substring(0, 4) + "…" : id;
    }
}
