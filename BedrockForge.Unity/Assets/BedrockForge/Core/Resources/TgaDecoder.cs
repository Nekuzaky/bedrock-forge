using System;

namespace BedrockForge.Resources
{
    /// <summary>Decodes the TGA variants found in Bedrock packs: true-color, raw or RLE, 24 or 32 bits.</summary>
    public static class TgaDecoder
    {
        /// <summary>Returns RGBA pixels, rows from top to bottom.</summary>
        public static (int width, int height, byte[] rgba) Decode(byte[] data)
        {
            int idLength = data[0];
            int colorMapType = data[1];
            int imageType = data[2];
            int colorMapLength = data[5] | data[6] << 8;
            int colorMapEntryBits = data[7];
            int width = data[12] | data[13] << 8;
            int height = data[14] | data[15] << 8;
            int bpp = data[16];
            int descriptor = data[17];

            if (imageType != 2 && imageType != 10)
                throw new NotSupportedException($"TGA image type {imageType} is not supported.");
            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException($"TGA with {bpp} bits per pixel is not supported.");

            int bytesPerPixel = bpp / 8;
            int p = 18 + idLength + (colorMapType == 1 ? colorMapLength * ((colorMapEntryBits + 7) / 8) : 0);
            var pixels = new byte[width * height * 4];
            int count = width * height;
            int i = 0;

            void Put(int src)
            {
                int d = i * 4;
                pixels[d] = data[src + 2];
                pixels[d + 1] = data[src + 1];
                pixels[d + 2] = data[src];
                pixels[d + 3] = bytesPerPixel == 4 ? data[src + 3] : (byte)255;
                i++;
            }

            if (imageType == 2)
            {
                for (; i < count; p += bytesPerPixel)
                    Put(p);
            }
            else
            {
                while (i < count)
                {
                    int header = data[p++];
                    int run = (header & 0x7F) + 1;
                    if ((header & 0x80) != 0)
                    {
                        for (int k = 0; k < run && i < count; k++)
                            Put(p);
                        p += bytesPerPixel;
                    }
                    else
                    {
                        for (int k = 0; k < run && i < count; k++, p += bytesPerPixel)
                            Put(p);
                    }
                }
            }

            // Bit 5 set = rows stored top to bottom; otherwise bottom to top.
            bool topDown = (descriptor & 0x20) != 0;
            if (!topDown)
                FlipRows(pixels, width, height);

            return (width, height, pixels);
        }

        private static void FlipRows(byte[] pixels, int width, int height)
        {
            int stride = width * 4;
            var tmp = new byte[stride];
            for (int y = 0; y < height / 2; y++)
            {
                int a = y * stride, b = (height - 1 - y) * stride;
                Buffer.BlockCopy(pixels, a, tmp, 0, stride);
                Buffer.BlockCopy(pixels, b, pixels, a, stride);
                Buffer.BlockCopy(tmp, 0, pixels, b, stride);
            }
        }
    }
}
