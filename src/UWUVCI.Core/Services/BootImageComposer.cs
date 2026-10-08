using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GameBaseClassLibrary;
using SkiaSharp;
using UWUVCI.Core.Tools;

namespace UWUVCI.Core.Services
{
    /// <summary>A frame the boot image creator can draw a title screenshot into.</summary>
    public sealed class FrameStyle
    {
        public string Name;       // shown to the user
        public string Resource;   // file name in Resources
        public string Layout;     // which screenshot rectangle to use
        public bool ShowsText = true;
    }

    /// <summary>
    /// Draws boot screens (1280x720) and menu icons (128x128) from a screenshot, in the style of the official
    /// Virtual Console ones. Port of BootImage.cs / MenuIconImage.cs from System.Drawing to Skia.
    /// </summary>
    public static class BootImageComposer
    {
        private static readonly Dictionary<string, SKRectI> TvRectangles = new Dictionary<string, SKRectI>
        {
            { "GBA", SKRectI.Create(132, 260, 399, 266) },
            { "GBC", SKRectI.Create(183, 260, 296, 266) },
            { "H4V3", SKRectI.Create(131, 249, 400, 300) },
            { "WII", SKRectI.Create(224, 200, 832, 333) },
        };

        private static readonly Dictionary<string, SKRectI> IconRectangles = new Dictionary<string, SKRectI>
        {
            { "GBA", SKRectI.Create(3, 17, 122, 81) },
            { "H4V3", SKRectI.Create(3, 9, 122, 92) },
            { "WII", SKRectI.Create(0, 23, 128, 94) },
        };

        /// <summary>The frames offered for a console; the first one is the default.</summary>
        public static List<FrameStyle> StylesFor(GameConsoles console)
        {
            FrameStyle S(string name, string resource, string layout = "H4V3", bool text = true) =>
                new FrameStyle { Name = name, Resource = resource, Layout = layout, ShowsText = text };

            switch (console)
            {
                case GameConsoles.NDS: return new List<FrameStyle> { S("Nintendo DS", "NDS.png") };
                case GameConsoles.N64: return new List<FrameStyle> { S("Nintendo 64", "N64.png") };
                case GameConsoles.NES: return new List<FrameStyle> { S("NES", "NES.png") };
                case GameConsoles.MSX: return new List<FrameStyle> { S("MSX", "MSX.png") };
                case GameConsoles.GBA:
                    return new List<FrameStyle>
                    {
                        S("Game Boy Advance", "GBA.png", "GBA"),
                        S("Game Boy Color", "GBC.png", "GBC"),
                        S("Game Boy", "newgameboy.png", "GBC"),
                    };
                case GameConsoles.SNES:
                    return new List<FrameStyle> { S("SNES (PAL)", "SNES-PAL.png"), S("SNES (USA)", "SNES-USA.png"), S("Super Famicom", "SFAM.png") };
                case GameConsoles.TG16:
                    return new List<FrameStyle> { S("TurboGrafx-16", "TG16.png"), S("TurboGrafx-CD", "TGCD.png") };
                case GameConsoles.GCN: return new List<FrameStyle> { S("GameCube", "GCN.png") };
                default:
                    return new List<FrameStyle>
                    {
                        S("Wii", "WII.png", "WII", text: false),
                        S("WiiWare", "WIIWARE.png", "WII", text: false),
                        S("Homebrew", "homebrew3.png", "WII", text: false),
                        S("Wii (with title)", "wii3New.png"),
                    };
            }
        }

        public static SKBitmap LoadResource(string name)
        {
            var assembly = typeof(BootImageComposer).Assembly;
            string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                return null;
            using Stream stream = assembly.GetManifestResourceStream(resource);
            return SKBitmap.Decode(stream);
        }

        private static SKTypeface LoadTypeface()
        {
            string font = Path.Combine(AppPaths.Tools, "font.otf");
            SKTypeface typeface = File.Exists(font) ? SKTypeface.FromFile(font) : null;
            return typeface ?? SKTypeface.FromFamilyName("Trebuchet MS", SKFontStyle.Bold) ?? SKTypeface.Default;
        }

        private static bool ContainsJapanese(string text) =>
            !string.IsNullOrEmpty(text) && text.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter);

        private static void DrawOutlinedText(SKCanvas canvas, string text, SKTypeface typeface, float size, bool bold, SKRect box, float shadowWidth, float outlineWidth)
        {
            if (string.IsNullOrEmpty(text))
                return;

            using var paint = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, FakeBoldText = bold && !typeface.IsBold };
            // Characters the bundled font lacks (mostly Japanese) come from a system font instead of showing as boxes.
            if (text.Any(c => c > 127 && !paint.ContainsGlyphs(c.ToString())))
            {
                SKTypeface fallback = SKFontManager.Default.MatchCharacter(text.First(c => c > 127 && !paint.ContainsGlyphs(c.ToString())));
                if (fallback != null)
                    paint.Typeface = fallback;
            }

            // GDI+ lays the text out from the top-left of the box with a small inset; Skia draws from the baseline.
            float x = box.Left + size / 6f;
            float y = box.Top - paint.FontMetrics.Ascent;
            using SKPath path = paint.GetTextPath(text, x, y);

            using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            stroke.Color = new SKColor(190, 190, 190);
            stroke.StrokeWidth = shadowWidth;
            canvas.DrawPath(path, stroke);
            stroke.Color = new SKColor(222, 222, 222);
            stroke.StrokeWidth = outlineWidth;
            canvas.DrawPath(path, stroke);

            using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true, Color = new SKColor(32, 32, 32) };
            canvas.DrawPath(path, fill);
        }

        /// <summary>Creates a 1280x720 boot screen. Any of the arguments may be null / 0 to leave that part out.</summary>
        public static SKBitmap CreateTv(FrameStyle style, SKBitmap titleScreen, string nameLine1, string nameLine2, int released, int players)
        {
            var image = new SKBitmap(new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(image);
            canvas.Clear(SKColors.White);
            using var quality = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };

            SKRectI rectangle = TvRectangles.TryGetValue(style.Layout, out SKRectI found) ? found : TvRectangles["H4V3"];
            if (titleScreen != null)
                canvas.DrawBitmap(titleScreen, rectangle, quality);
            else
                canvas.DrawRect(rectangle, new SKPaint { Color = SKColors.Black });

            using (SKBitmap frame = LoadResource(style.Resource))
                if (frame != null)
                    canvas.DrawBitmap(frame, SKRect.Create(0, 0, 1280, 720), quality);

            if (!style.ShowsText)
                return image;

            using SKTypeface typeface = LoadTypeface();
            // Sizes are the GDI+ point sizes of the original at 96 dpi.
            const float titleSize = 96f * 37f / 72f, detailSize = 96f * 25f / 72f;
            bool japanese = ContainsJapanese(nameLine1) || ContainsJapanese(nameLine2);
            bool longName = !string.IsNullOrWhiteSpace(nameLine2);

            DrawOutlinedText(canvas, nameLine1, typeface, titleSize, true, SKRect.Create(578, longName ? 313 : 340, 640, 50), 7, 5);
            if (longName)
                DrawOutlinedText(canvas, nameLine2, typeface, titleSize, true, SKRect.Create(578, 368, 640, 50), 7, 5);

            if (released > 0)
                DrawOutlinedText(canvas, japanese ? released + "年発売" : "Released: " + released, typeface, detailSize, false, SKRect.Create(586, 450, 600, 40), 6, 4);

            if (players > 0)
            {
                string count = players >= 4 ? "1-4" : (players == 1 ? "1" : "1-" + players);
                DrawOutlinedText(canvas, japanese ? "プレイ人数　" + count + "人" : "Players: " + count, typeface, detailSize, false, SKRect.Create(586, 496, 600, 40), 6, 4);
            }

            return image;
        }

        /// <summary>Creates a 128x128 menu icon from a screenshot.</summary>
        public static SKBitmap CreateIcon(GameConsoles console, SKBitmap titleScreen)
        {
            string resource = "Icon.png", layout = "H4V3";
            if (console == GameConsoles.GBA) layout = "GBA";
            if (console == GameConsoles.WII) { resource = "WiiIcon.png"; layout = "WII"; }
            if (console == GameConsoles.GCN) { resource = "GCNIcon.png"; layout = "WII"; }

            var image = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(image);
            canvas.Clear(new SKColor(30, 30, 30));
            using var quality = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };

            SKRectI rectangle = IconRectangles[layout];
            if (titleScreen != null)
                canvas.DrawBitmap(titleScreen, rectangle, quality);
            else
                canvas.DrawRect(rectangle, new SKPaint { Color = SKColors.Black });

            using (SKBitmap frame = LoadResource(resource))
                if (frame != null)
                    canvas.DrawBitmap(frame, SKRect.Create(0, 0, 128, 128), quality);
            return image;
        }

        /// <summary>Saves a created image where the app keeps them and returns the path.</summary>
        public static string Save(SKBitmap image, string name)
        {
            Directory.CreateDirectory(AppPaths.CreatedImages);
            string path = Path.Combine(AppPaths.CreatedImages, name + ".png");
            ImageTools.SavePng(image, path);
            return path;
        }
    }
}
