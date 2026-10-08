using System;
using System.IO;
using SkiaSharp;

namespace UWUVCI.Core.Tools
{
    /// <summary>
    /// Replaces png2tga / jpg2tga / bmp2tga / tga2png / tga_verify: reads PNG, JPG, BMP and TGA and writes the plain
    /// uncompressed TGAs the Wii U menu expects.
    /// </summary>
    public static class ImageTools
    {
        public static bool IsTga(string path) => path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);

        /// <summary>Loads any supported image as premultiplied BGRA.</summary>
        public static SKBitmap Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            SKBitmap bitmap = IsTga(path) ? DecodeTga(data) : SKBitmap.Decode(data);
            if (bitmap == null && !IsTga(path))
                bitmap = TryDecodeTga(data);
            if (bitmap == null)
                throw new InvalidDataException($"Could not read image {Path.GetFileName(path)}");
            return bitmap;
        }

        public static SKBitmap Load(byte[] data, string extension)
        {
            bool tga = extension != null && extension.Contains("tga", StringComparison.OrdinalIgnoreCase);
            SKBitmap bitmap = tga ? DecodeTga(data) : (SKBitmap.Decode(data) ?? TryDecodeTga(data));
            if (bitmap == null)
                throw new InvalidDataException("Could not read image data.");
            return bitmap;
        }

        private static SKBitmap TryDecodeTga(byte[] data)
        {
            try { return DecodeTga(data); }
            catch { return null; }
        }

        /// <summary>Converts an image to a TGA of exactly the given size and bit depth (24 or 32).</summary>
        public static void ConvertToTga(string inputPath, string outputPath, int width, int height, int bitDepth)
        {
            using SKBitmap source = Load(inputPath);
            using SKBitmap sized = Resize(source, width, height);
            SaveTga(sized, outputPath, bitDepth);
        }

        public static void ConvertToPng(string inputPath, string outputPath)
        {
            using SKBitmap source = Load(inputPath);
            SavePng(source, outputPath);
        }

        public static void SavePng(SKBitmap bitmap, string outputPath)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(outputPath);
            data.SaveTo(stream);
        }

        public static SKBitmap Resize(SKBitmap source, int width, int height)
        {
            // Canvases can only draw into premultiplied bitmaps; GetPixel hands back straight alpha either way.
            var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (source.Width == width && source.Height == height)
            {
                using var canvasCopy = new SKCanvas(target);
                canvasCopy.Clear(SKColors.Transparent);
                using var paintCopy = new SKPaint { BlendMode = SKBlendMode.Src };
                canvasCopy.DrawBitmap(source, 0, 0, paintCopy);
                return target;
            }

            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true, BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
            return target;
        }

        /// <summary>Writes an uncompressed true-colour TGA (bottom-left origin) with the TGA 2.0 footer.</summary>
        public static void SaveTga(SKBitmap bitmap, string outputPath, int bitDepth)
        {
            if (bitDepth != 24 && bitDepth != 32)
                throw new ArgumentException("TGA bit depth must be 24 or 32.", nameof(bitDepth));

            int width = bitmap.Width, height = bitmap.Height, bytesPerPixel = bitDepth / 8;
            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            var header = new byte[18];
            header[2] = 2;
            header[12] = (byte)width;
            header[13] = (byte)(width >> 8);
            header[14] = (byte)height;
            header[15] = (byte)(height >> 8);
            header[16] = (byte)bitDepth;
            header[17] = (byte)(bitDepth == 32 ? 8 : 0);
            stream.Write(header, 0, header.Length);

            var row = new byte[width * bytesPerPixel];
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    SKColor c = bitmap.GetPixel(x, y);
                    int o = x * bytesPerPixel;
                    if (bitDepth == 24 && c.Alpha != 255)
                    {
                        // No alpha channel to carry it: flatten onto black like the original converters do.
                        row[o] = (byte)(c.Blue * c.Alpha / 255);
                        row[o + 1] = (byte)(c.Green * c.Alpha / 255);
                        row[o + 2] = (byte)(c.Red * c.Alpha / 255);
                    }
                    else
                    {
                        row[o] = c.Blue;
                        row[o + 1] = c.Green;
                        row[o + 2] = c.Red;
                        if (bitDepth == 32)
                            row[o + 3] = c.Alpha;
                    }
                }
                stream.Write(row, 0, row.Length);
            }

            stream.Write(new byte[8], 0, 8);
            byte[] signature = System.Text.Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0");
            stream.Write(signature, 0, signature.Length);
        }

        public struct TgaInfo
        {
            public int Width, Height, BitDepth, ImageType;
        }

        public static TgaInfo ReadTgaInfo(string path)
        {
            var header = new byte[18];
            using (var stream = File.OpenRead(path))
                if (stream.Read(header, 0, 18) != 18)
                    throw new InvalidDataException("TGA file is too short.");
            return new TgaInfo
            {
                ImageType = header[2],
                Width = header[12] | header[13] << 8,
                Height = header[14] | header[15] << 8,
                BitDepth = header[16],
            };
        }

        /// <summary>Decodes true-colour and greyscale TGAs, raw or RLE, 8/15/16/24/32 bit.</summary>
        public static SKBitmap DecodeTga(byte[] data)
        {
            if (data.Length < 18)
                throw new InvalidDataException("TGA file is too short.");

            int idLength = data[0], colorMapType = data[1], imageType = data[2];
            int colorMapLength = data[5] | data[6] << 8, colorMapEntrySize = data[7];
            int width = data[12] | data[13] << 8, height = data[14] | data[15] << 8;
            int bitDepth = data[16], descriptor = data[17];
            bool rle = imageType == 10 || imageType == 11 || imageType == 9;
            int baseType = rle ? imageType - 8 : imageType;
            if (baseType < 1 || baseType > 3 || width == 0 || height == 0)
                throw new InvalidDataException("Unsupported TGA image type.");

            int bytesPerPixel = (bitDepth + 7) / 8;
            int offset = 18 + idLength;
            byte[] palette = null;
            int paletteEntryBytes = (colorMapEntrySize + 7) / 8;
            if (colorMapType == 1)
            {
                palette = new byte[colorMapLength * paletteEntryBytes];
                Buffer.BlockCopy(data, offset, palette, 0, Math.Min(palette.Length, data.Length - offset));
                offset += palette.Length;
            }
            if (baseType == 1 && palette == null)
                throw new InvalidDataException("Colour-mapped TGA without a colour map.");

            var pixels = new byte[width * height * bytesPerPixel];
            if (!rle)
                Buffer.BlockCopy(data, offset, pixels, 0, Math.Min(pixels.Length, data.Length - offset));
            else
            {
                int written = 0;
                while (written < pixels.Length && offset < data.Length)
                {
                    int packet = data[offset++];
                    int count = (packet & 0x7F) + 1;
                    if ((packet & 0x80) != 0)
                    {
                        for (int i = 0; i < count && written < pixels.Length; i++, written += bytesPerPixel)
                            Buffer.BlockCopy(data, offset, pixels, written, bytesPerPixel);
                        offset += bytesPerPixel;
                    }
                    else
                    {
                        int length = Math.Min(count * bytesPerPixel, pixels.Length - written);
                        Buffer.BlockCopy(data, offset, pixels, written, length);
                        offset += length;
                        written += length;
                    }
                }
            }

            bool topDown = (descriptor & 0x20) != 0, rightToLeft = (descriptor & 0x10) != 0;
            bool hasAlpha = bitDepth == 32;
            bool anyAlpha = false;
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

            SKColor Decode(byte[] source, int at, int bytes, int bits)
            {
                switch (bytes)
                {
                    case 1:
                        return new SKColor(source[at], source[at], source[at]);
                    case 2:
                        int v = source[at] | source[at + 1] << 8;
                        return new SKColor((byte)(((v >> 10) & 31) * 255 / 31), (byte)(((v >> 5) & 31) * 255 / 31), (byte)((v & 31) * 255 / 31));
                    case 3:
                        return new SKColor(source[at + 2], source[at + 1], source[at]);
                    default:
                        return new SKColor(source[at + 2], source[at + 1], source[at], bits == 32 && hasAlpha ? source[at + 3] : (byte)255);
                }
            }

            for (int y = 0; y < height; y++)
            {
                int destY = topDown ? y : height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int destX = rightToLeft ? width - 1 - x : x;
                    int at = (y * width + x) * bytesPerPixel;
                    SKColor color;
                    if (baseType == 1)
                    {
                        int index = bytesPerPixel == 1 ? pixels[at] : pixels[at] | pixels[at + 1] << 8;
                        color = Decode(palette, index * paletteEntryBytes, paletteEntryBytes, colorMapEntrySize);
                    }
                    else
                        color = Decode(pixels, at, bytesPerPixel, bitDepth);
                    anyAlpha |= color.Alpha != 0;
                    bitmap.SetPixel(destX, destY, color);
                }
            }

            // Some writers leave the alpha channel of 32 bit files zeroed; treat those as opaque rather than invisible.
            if (hasAlpha && !anyAlpha)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        bitmap.SetPixel(x, y, bitmap.GetPixel(x, y).WithAlpha(255));
            return bitmap;
        }
    }
}
