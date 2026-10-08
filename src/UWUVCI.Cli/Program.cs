using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameBaseClassLibrary;
using SkiaSharp;
using UWUVCI.Core;
using UWUVCI.Core.Engine;
using UWUVCI.Core.Services;
using UWUVCI.Core.Tools;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Helpers;
using UWUVCI_AIO_WPF.Models;

namespace UWUVCI.Cli
{
    /// <summary>Command line front end for the injection engine. Mainly a test harness; the app is the real front end.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("usage: uwuvci-cli <setup|bases|selftest|rominfo|inject> [options]");
                return 2;
            }

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                    continue;
                string key = args[i].Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    options[key] = args[++i];
                else
                    flags.Add(key);
            }
            string Opt(string name, string fallback = null) => options.TryGetValue(name, out string v) ? v : fallback;

            if (Opt("home") != null)
                AppPaths.SetDataRoot(Opt("home"));
            AppPaths.EnsureCreated();
            JsonSettingsManager.LoadSettings();
            Logger.Logged += line => { if (flags.Contains("verbose")) Console.WriteLine("  log: " + line); };

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "setup":
                    {
                        var progress = new Progress<(int, string)>(p => Console.WriteLine($"  {p.Item1,3}% {p.Item2}"));
                        ToolManager.DownloadMissingAsync(progress).GetAwaiter().GetResult();
                        BaseManager.DownloadListsAsync(false, progress).GetAwaiter().GetResult();
                        Console.WriteLine("Data folder: " + AppPaths.DataRoot);
                        Console.WriteLine("Missing tools: " + ToolManager.MissingTools().Count);
                        return 0;
                    }
                    case "bases":
                    {
                        foreach (GameConsoles console in BaseManager.ListedConsoles)
                        {
                            List<GameBases> bases = BaseManager.GetBases(console);
                            Console.WriteLine($"{console}: {bases.Count} bases");
                            foreach (GameBases b in bases)
                                Console.WriteLine($"   {b.Name} [{b.Region}] tid={b.Tid} hash={b.KeyHash} downloaded={BaseManager.IsDownloaded(b)}");
                        }
                        return 0;
                    }
                    case "rominfo":
                    {
                        var console = Enum.Parse<GameConsoles>(Opt("console"), true);
                        RomDetails details = RomInfo.Read(Opt("rom"), console);
                        Console.WriteLine($"name='{details.InternalName}' ids={string.Join(", ", details.RepoIds)}");
                        RepoArtwork art = RomInfo.FindArtworkAsync(details, console).GetAwaiter().GetResult();
                        Console.WriteLine(art == null ? "no artwork found" : $"icon={art.IconUrl}\ntv={art.TvUrl}\nini={art.IniUrl}\nsound={art.BootSoundUrl}");
                        return 0;
                    }
                    case "selftest":
                        return SelfTest.Run(Opt("work", Path.Combine(AppPaths.DataRoot, "selftest")));
                    case "inject":
                        return Inject(Opt, flags);
                    // Single tools, for comparing against the originals.
                    case "getext":
                        Console.WriteLine(RomTools.PatchGetExtType(Opt("file")) ? "patched" : "nothing patched");
                        return 0;
                    case "icon":
                    {
                        // Used by the build script: turns the Windows .ico into the PNG the macOS icon is made from.
                        using SKBitmap source = SKBitmap.Decode(Opt("in"));
                        int size = int.Parse(Opt("size", "1024"));
                        using var canvas = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                        using (var draw = new SKCanvas(canvas))
                        {
                            draw.Clear(SKColors.Transparent);
                            // macOS icons sit inside a margin; a full-bleed image looks oversized in the Dock.
                            float inset = size * 0.1f;
                            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                            draw.DrawBitmap(source, SKRect.Create(inset, inset, size - 2 * inset, size - 2 * inset), paint);
                        }
                        ImageTools.SavePng(canvas, Opt("out"));
                        Console.WriteLine($"{source.Width}x{source.Height} -> {size}x{size}");
                        return 0;
                    }
                    case "nkit":
                        Console.WriteLine(DiscTools.NKitConvert(Opt("file"), flags.Contains("iso")));
                        return 0;
                    case "rpx":
                        if (flags.Contains("compress")) RpxTool.Compress(Opt("file")); else RpxTool.Decompress(Opt("file"));
                        return 0;
                    default:
                        Console.WriteLine("unknown command " + args[0]);
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex.Message);
                if (flags.Contains("verbose"))
                    Console.WriteLine(ex);
                return 1;
            }
        }

        private static int Inject(Func<string, string, string> Opt, HashSet<string> flags)
        {
            var console = Enum.Parse<GameConsoles>(Opt("console", null), true);
            bool gc = console == GameConsoles.GCN;

            var config = new GameConfig
            {
                Console = gc ? GameConsoles.WII : console,
                BaseRom = BaseManager.CustomBase(),
                CBasePath = Opt("base", null),
                GameName = Opt("name", "Test Inject"),
            };
            config.TGAIco.ImgPath = Opt("icon", null);
            config.TGATv.ImgPath = Opt("tv", null);
            config.TGADrc.ImgPath = Opt("drc", null);
            config.TGALog.ImgPath = Opt("logo", null);
            config.N64Stuff.INIPath = Opt("ini", null);
            config.N64Stuff.DarkFilter = flags.Contains("n64-remove-darkfilter");
            config.N64Stuff.WideScreen = flags.Contains("widescreen");
            config.GBAStuff.DarkFilter = !flags.Contains("gba-remove-darkfilter");

            var context = new InjectContext
            {
                GC = gc,
                saveworkaround = flags.Contains("nospacecheck"),
                donttrim = flags.Contains("donttrim"),
                Index = int.Parse(Opt("gamepad", "-1")),
                LR = flags.Contains("lr"),
                passtrough = !flags.Contains("nopassthrough"),
                GctPath = Opt("gct", ""),
                gc2rom = Opt("disc2", ""),
                PokePatch = flags.Contains("pokepatch"),
                RemoveDeflicker = flags.Contains("deflicker"),
                RemoveDithering = flags.Contains("dithering"),
                HalfVFilter = flags.Contains("halfvfilter"),
                regionfrii = Opt("region", null) != null,
                regionfriius = Opt("region", "") == "us",
                regionfriijp = Opt("region", "") == "jp",
                BootSound = Opt("sound", ""),
                DSLayout = flags.Contains("dslayout"),
                Brightness = int.Parse(Opt("brightness", "80")),
            };
            int last = -1;
            context.ProgressChanged += (percent, message) =>
            {
                if (percent != last && !string.IsNullOrEmpty(message))
                    Console.WriteLine($"  {percent,3}% {message}");
                last = percent;
            };

            if (Opt("out", null) != null)
                JsonSettingsManager.Settings.OutPath = Path.GetFullPath(Opt("out", null));
            if (Opt("ckey", null) != null)
                JsonSettingsManager.Settings.Ckey = Opt("ckey", null);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Injection.Inject(config, Opt("rom", null), context, flags.Contains("force43"));
            Console.WriteLine($"Injected in {watch.Elapsed.TotalSeconds:0.0}s");
            foreach (string warning in context.Warnings)
                Console.WriteLine("  warning: " + warning);

            string consoleName = gc ? "GCN" : console.ToString();
            string output = flags.Contains("loadiine")
                ? Injection.Loadiine(config.GameName, consoleName, context)
                : Injection.Packing(config.GameName, consoleName, context);
            Console.WriteLine($"Done in {watch.Elapsed.TotalSeconds:0.0}s -> {output}");
            return 0;
        }
    }
}
