using System;
using System.Collections.Generic;
using BedrockForge.Resources;
using UnityEngine;

namespace BedrockForge.Rendering.Blocks
{
    /// <summary>
    /// Collects the block textures the world actually uses and packs them into one 16×16 <see cref="Texture2DArray"/>.
    /// Slices are handed out from worker threads; <see cref="Build"/> decodes the images on the main thread.
    /// </summary>
    public sealed class TextureCatalog
    {
        public const int Size = 16;
        public const int WhiteSlice = 0;
        public const string GlobalTextureName = "_BedrockForgeBlocks";

        private readonly object _lock = new object();
        private readonly ResourcePackStack? _packs;
        private readonly Dictionary<string, int> _slices = new Dictionary<string, int>();
        private readonly List<Request> _requests = new List<Request> { new Request(null, null, null) };

        private sealed class Request
        {
            public readonly string? ImagePath;
            public readonly Color? Overlay;
            public readonly Color? Tint;

            public Request(string? imagePath, Color? overlay, Color? tint)
            {
                ImagePath = imagePath;
                Overlay = overlay;
                Tint = tint;
            }
        }

        public TextureCatalog(ResourcePackStack? packs)
        {
            _packs = packs;
        }

        public ResourcePackStack? Packs => _packs;

        public int Count
        {
            get { lock (_lock) return _requests.Count; }
        }

        /// <summary>
        /// Slice for a texture reference, or -1 if its image is missing. <paramref name="grayTint"/> colors
        /// grayscale textures (grass, leaves, water), which Bedrock tints per biome.
        /// </summary>
        public int GetSlice(TextureRef texture, Color? grayTint)
        {
            if (_packs == null)
                return -1;
            string? image = _packs.FindImage(texture.Path);
            if (image == null)
                return -1;

            Color? overlay = ParseHex(texture.OverlayColor);
            Color? tint = ParseHex(texture.TintColor) ?? grayTint;
            string key = $"{image}|{overlay}|{tint}";

            lock (_lock)
            {
                if (_slices.TryGetValue(key, out int slice))
                    return slice;
                slice = _requests.Count;
                _requests.Add(new Request(image, overlay, tint));
                _slices[key] = slice;
                return slice;
            }
        }

        /// <summary>Decodes every requested image into a texture array. Main thread only.</summary>
        public Texture2DArray Build()
        {
            Request[] requests;
            lock (_lock) requests = _requests.ToArray();

            var array = new Texture2DArray(Size, Size, requests.Length, TextureFormat.RGBA32, true, false)
            {
                name = "Bedrock Forge Blocks",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };

            var white = new Color32[Size * Size];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);

            for (int i = 0; i < requests.Length; i++)
            {
                Color32[] pixels = white;
                var r = requests[i];
                if (r.ImagePath != null)
                {
                    try
                    {
                        pixels = Load(r);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bedrock Forge] Cannot load {r.ImagePath}: {ex.Message}");
                    }
                }
                array.SetPixels32(pixels, i, 0);
            }

            array.Apply(true, false);
            return array;
        }

        private Color32[] Load(Request r)
        {
            byte[] bytes = _packs!.ReadFile(r.ImagePath!) ?? throw new InvalidOperationException("file vanished");
            int width, height;
            Color32[] source;

            if (r.ImagePath!.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                var (w, h, rgba) = TgaDecoder.Decode(bytes);
                width = w;
                height = h;
                // TGA decoder gives rows top to bottom; Unity wants bottom to top.
                source = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int s = ((h - 1 - y) * w + x) * 4;
                        source[y * w + x] = new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                    }
            }
            else
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!tex.LoadImage(bytes))
                        throw new InvalidOperationException("not a PNG");
                    width = tex.width;
                    height = tex.height;
                    source = tex.GetPixels32();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            // Animated textures are vertical strips of square frames: keep the top (first) frame.
            int frame = Math.Min(width, height);
            int frameTop = height - frame;
            bool grayscale = IsGrayscale(source);

            var result = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int sx = x * frame / Size;
                    int sy = frameTop + y * frame / Size;
                    var c = source[sy * width + sx];

                    if (r.Overlay.HasValue)
                    {
                        // Overlay textures (grass side): alpha marks the part to tint, the rest stays as is.
                        float a = c.a / 255f;
                        var tinted = Multiply(c, r.Overlay.Value);
                        c = Color32.Lerp(new Color32(c.r, c.g, c.b, 255), tinted, a);
                        c.a = 255;
                    }
                    else if (r.Tint.HasValue && grayscale)
                    {
                        byte alpha = c.a;
                        c = Multiply(c, r.Tint.Value);
                        c.a = alpha;
                    }
                    result[y * Size + x] = c;
                }
            return result;
        }

        private static Color32 Multiply(Color32 c, Color tint) =>
            new Color32((byte)(c.r * tint.r), (byte)(c.g * tint.g), (byte)(c.b * tint.b), 255);

        private static bool IsGrayscale(Color32[] pixels)
        {
            foreach (var c in pixels)
                if (c.a > 0 && (Math.Abs(c.r - c.g) > 8 || Math.Abs(c.g - c.b) > 8))
                    return false;
            return true;
        }

        private static Color? ParseHex(string? hex) =>
            hex != null && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : (Color?)null;
    }
}
