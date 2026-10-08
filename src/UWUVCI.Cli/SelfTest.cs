using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameBaseClassLibrary;
using GMWare.M2.MArchive;
using GMWare.M2.Psb;
using Newtonsoft.Json.Linq;
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
    /// <summary>
    /// Checks of the managed tool replacements that need no copyrighted input. Files are left in the work folder so
    /// they can also be compared against the original tools.
    /// </summary>
    internal static class SelfTest
    {
        private static int failures;

        private static void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}{(detail.Length > 0 ? " - " + detail : "")}");
            if (!ok)
                failures++;
        }

        private static void Section(string name, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check(name, false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static int Run(string work)
        {
            if (Directory.Exists(work))
                Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            failures = 0;

            Section("legacy files", () => LegacyFiles(work));
            Section("rpx", () => Rpx(work));
            Section("retroinject", () => Retro(work));
            Section("n64", () => N64(work));
            Section("images", () => Images(work));
            Section("boot sound", () => BootSound(work));
            Section("gba archive", () => Gba(work));
            Section("pce pkg", () => Pce(work));
            Section("nus packer", () => Pack(work));

            Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static byte[] Pattern(int length, int seed)
        {
            // Compressible but not trivial.
            var random = new Random(seed);
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(i % 97 < 60 ? i % 97 : random.Next(256));
            return data;
        }

        private static void LegacyFiles(string work)
        {
            var config = new GameConfig { Console = GameConsoles.N64, GameName = "Round|Trip", Index = 3, donttrim = true };
            config.BaseRom = new GameBases { Name = "Paper Mario", Region = Regions.US, Tid = "0005000010199800", KeyHash = 42 };
            config.TGAIco.ImgBin = new byte[] { 1, 2, 3 };
            config.TGAIco.extension = "png";
            config.N64Stuff.WideScreen = true;
            string file = Path.Combine(work, "test.uwuvci");
            LegacySerializer.Write(file, config);
            GameConfig back = LegacySerializer.Read<GameConfig>(file);
            Check("config round trip", back.GameName == config.GameName && back.Index == 3 && back.donttrim && back.BaseRom.Tid == config.BaseRom.Tid
                && back.TGAIco.ImgBin.SequenceEqual(config.TGAIco.ImgBin) && back.N64Stuff.WideScreen);

            // The file must name the Windows app's assembly, or the Windows app could not open it.
            using (var gzip = new System.IO.Compression.GZipStream(File.OpenRead(file), System.IO.Compression.CompressionMode.Decompress))
            using (var raw = new MemoryStream())
            {
                gzip.CopyTo(raw);
                string text = Encoding.ASCII.GetString(raw.ToArray());
                Check("config uses Windows type names", text.Contains("UWUVCI AIO WPF, Version=") && text.Contains("UWUVCI_AIO_WPF.GameConfig") && !text.Contains("UWUVCI.Core"));
            }

            var keys = new List<TKeys> { new TKeys { Base = config.BaseRom, Tkey = "abc" }, new TKeys { Base = new GameBases { Name = "x" } } };
            string keyFile = Path.Combine(work, "test.vck");
            LegacySerializer.Write(keyFile, keys);
            List<TKeys> keysBack = LegacySerializer.Read<List<TKeys>>(keyFile);
            Check("key file round trip", keysBack.Count == 2 && keysBack[0].Tkey == "abc" && keysBack[1].Tkey == null);

            int lists = BaseManager.ListedConsoles.Count(c => BaseManager.GetBases(c).Count > 0);
            Check("downloaded base lists readable", lists == BaseManager.ListedConsoles.Length || BaseManager.MissingLists().Count > 0,
                $"{lists}/{BaseManager.ListedConsoles.Length} lists have entries (run 'setup' first to test this)");
        }

        private static byte[] BuildRpx()
        {
            // null, .text (large), .data (tiny, not worth compressing), .rodata (medium), crcs, fileinfo
            byte[][] bodies = { null, Pattern(70000, 1), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Pattern(5000, 2), new byte[6 * 4], Pattern(0x60, 3) };
            uint[] types = { 0, 1, 1, 1, 0x80000003, 0x80000004 };
            const int shoff = 0x40, shentsize = 0x28;
            using var ms = new MemoryStream();
            var header = new byte[0x40];
            new byte[] { 0x7F, 0x45, 0x4C, 0x46, 1, 2, 1, 0xCA, 0xFE }.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x10), 0xFE01);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x12), 0x14);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x14), 1);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x18), 0x02000000);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x20), shoff);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x28), 0x34);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x2E), shentsize);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0x30), (ushort)bodies.Length);
            ms.Write(header);
            ms.Write(new byte[bodies.Length * shentsize]);

            var offsets = new uint[bodies.Length];
            for (int i = 1; i < bodies.Length; i++)
            {
                while (ms.Position % 0x40 != 0)
                    ms.WriteByte(0);
                offsets[i] = (uint)ms.Position;
                ms.Write(bodies[i]);
            }
            byte[] file = ms.ToArray();
            for (int i = 0; i < bodies.Length; i++)
            {
                var s = file.AsSpan(shoff + i * shentsize);
                BinaryPrimitives.WriteUInt32BigEndian(s.Slice(4), types[i]);
                BinaryPrimitives.WriteUInt32BigEndian(s.Slice(12), (uint)(0x02000000 + i * 0x100000));
                BinaryPrimitives.WriteUInt32BigEndian(s.Slice(16), offsets[i]);
                BinaryPrimitives.WriteUInt32BigEndian(s.Slice(20), (uint)(bodies[i]?.Length ?? 0));
            }
            return file;
        }

        private static void Rpx(string work)
        {
            byte[] plain = BuildRpx();
            File.WriteAllBytes(Path.Combine(work, "synthetic.rpx"), plain);

            byte[] canonical = RpxTool.Convert(plain, compress: false);
            byte[] packed = RpxTool.Convert(canonical, compress: true);
            byte[] unpacked = RpxTool.Convert(packed, compress: false);
            File.WriteAllBytes(Path.Combine(work, "mine.decompressed.rpx"), canonical);
            File.WriteAllBytes(Path.Combine(work, "mine.compressed.rpx"), packed);

            Check("rpx compress makes it smaller", packed.Length < canonical.Length, $"{canonical.Length} -> {packed.Length}");
            Check("rpx round trip is lossless", unpacked.SequenceEqual(canonical));
            Check("rpx recompress is stable", RpxTool.Convert(unpacked, compress: true).SequenceEqual(packed));
        }

        private static void Retro(string work)
        {
            // A fake SNES Virtual Console image: size key 22 bytes before "WUP-", ROM 12 bytes after it.
            byte[] elf = Pattern(0x30000, 5);
            for (int i = 0; i + 4 <= elf.Length; i += 4)
                if (elf[i] == 0x57 && elf[i + 1] == 0x55 && elf[i + 2] == 0x50 && elf[i + 3] == 0x2D)
                    elf[i] = 0;
            const int wup = 0x1000;
            Encoding.ASCII.GetBytes("WUP-").CopyTo(elf, wup);
            elf[wup - 22] = 1;
            string elfPath = Path.Combine(work, "snes.elf"), romPath = Path.Combine(work, "snes.sfc"), outPath = Path.Combine(work, "mine.snes.elf");
            byte[] rom = Pattern(0x10000, 6);
            File.WriteAllBytes(elfPath, elf);
            File.WriteAllBytes(romPath, rom);
            RomTools.RetroInject(elfPath, romPath, outPath);
            byte[] result = File.ReadAllBytes(outPath);
            Check("retroinject places the ROM", result.Length == elf.Length && result.AsSpan(wup + 12, rom.Length).SequenceEqual(rom)
                && result.AsSpan(0, wup + 12).SequenceEqual(elf.AsSpan(0, wup + 12)));

            File.WriteAllBytes(romPath + ".big", Pattern(0x10001, 7));
            bool refused = false;
            try { RomTools.RetroInject(elfPath, romPath + ".big", outPath + ".big"); }
            catch (Exception ex) { refused = ex.Message == "retro"; }
            Check("retroinject refuses a ROM that is too large", refused);
        }

        private static void N64(string work)
        {
            byte[] z64 = Pattern(0x1000, 8);
            new byte[] { 0x80, 0x37, 0x12, 0x40 }.CopyTo(z64, 0);
            byte[] v64 = (byte[])z64.Clone();
            for (int i = 0; i < v64.Length; i += 2)
                (v64[i], v64[i + 1]) = (v64[i + 1], v64[i]);
            byte[] n64 = (byte[])z64.Clone();
            for (int i = 0; i < n64.Length; i += 4)
                Array.Reverse(n64, i, 4);

            foreach (var (name, data) in new[] { ("z64", z64), ("v64", v64), ("n64", n64) })
            {
                string input = Path.Combine(work, "rom." + name), output = Path.Combine(work, "rom." + name + ".out");
                File.WriteAllBytes(input, data);
                RomTools.ConvertN64(input, output);
                Check($"n64 {name} -> big endian", File.ReadAllBytes(output).SequenceEqual(z64));
            }
        }

        private static void Images(string work)
        {
            using var source = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(source))
            {
                canvas.Clear(new SKColor(20, 40, 160));
                canvas.DrawRect(SKRect.Create(0, 0, 160, 120), new SKPaint { Color = new SKColor(255, 0, 0) });
                canvas.DrawCircle(240, 180, 40, new SKPaint { Color = new SKColor(0, 255, 0), IsAntialias = true });
            }
            string png = Path.Combine(work, "shot.png");
            ImageTools.SavePng(source, png);

            foreach (var (name, w, h, bpp) in new[] { ("iconTex", 128, 128, 32), ("bootTvTex", 1280, 720, 24), ("bootDrcTex", 854, 480, 24), ("bootLogoTex", 170, 42, 32) })
            {
                string tga = Path.Combine(work, name + ".tga");
                ImageTools.ConvertToTga(png, tga, w, h, bpp);
                ImageTools.TgaInfo info = ImageTools.ReadTgaInfo(tga);
                long expected = 18 + (long)w * h * bpp / 8 + 26;
                using SKBitmap back = ImageTools.Load(tga);
                SKColor corner = back.GetPixel(2, 2), other = back.GetPixel(w - 3, 2);
                Check($"{name}.tga", info.Width == w && info.Height == h && info.BitDepth == bpp && info.ImageType == 2 && new FileInfo(tga).Length == expected
                    && corner.Red > 240 && corner.Blue < 20 && other.Blue > 140 && other.Red < 40,
                    $"{info.Width}x{info.Height} {info.BitDepth}bit, top-left={corner}");
            }

            // A TGA someone else made (top-down, RLE) must come out the right way up.
            string rle = Path.Combine(work, "rle.tga");
            using (var fs = File.Create(rle))
            {
                var header = new byte[18];
                header[2] = 10; header[12] = 4; header[14] = 2; header[16] = 24; header[17] = 0x20;
                fs.Write(header);
                fs.Write(new byte[] { 0x83, 0, 0, 255 });          // row 0: 4 x red (BGR)
                fs.Write(new byte[] { 0x03, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0 }); // row 1: 4 raw blue
            }
            using (SKBitmap decoded = ImageTools.Load(rle))
                Check("rle top-down tga decodes", decoded.GetPixel(0, 0).Red == 255 && decoded.GetPixel(3, 1).Blue == 255 && decoded.GetPixel(3, 1).Red == 0);

            foreach (GameConsoles console in new[] { GameConsoles.GBA, GameConsoles.SNES, GameConsoles.WII, GameConsoles.N64 })
            {
                FrameStyle style = BootImageComposer.StylesFor(console)[0];
                using SKBitmap tv = BootImageComposer.CreateTv(style, source, "Self Test Game", console == GameConsoles.N64 ? "Second Line" : null, 1998, 2);
                ImageTools.SavePng(tv, Path.Combine(work, $"boot-{console}.png"));
                using SKBitmap icon = BootImageComposer.CreateIcon(console, source);
                ImageTools.SavePng(icon, Path.Combine(work, $"icon-{console}.png"));
                Check($"boot image {console}", tv.Width == 1280 && tv.Height == 720 && icon.Width == 128);
            }
        }

        private static void BootSound(string work)
        {
            // Two seconds of 48 kHz 16 bit stereo.
            int samples = 48000 * 2;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples * 4); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(48000); w.Write(48000 * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples * 4);
                for (int i = 0; i < samples; i++)
                {
                    short v = (short)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 8000);
                    w.Write(v); w.Write(v);
                }
            }
            string wav = Path.Combine(work, "tone.wav"), btsnd = Path.Combine(work, "bootSound.btsnd");
            File.WriteAllBytes(wav, ms.ToArray());
            BootSoundTool.Create(wav, btsnd, work);
            byte[] result = File.ReadAllBytes(btsnd);
            byte[] original = ms.ToArray();
            Check("btsnd from wav", result.Length == 8 + samples * 4 && result[3] == 2 && result[8 + 100] == original[44 + 101] && result[8 + 101] == original[44 + 100]);

            if (AppPaths.IsMac)
            {
                // 44.1 kHz mono has to go through afconvert and come out as 48 kHz stereo.
                using var mono = new MemoryStream();
                using (var w = new BinaryWriter(mono, Encoding.ASCII, true))
                {
                    int n = 44100 * 8;
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(44100); w.Write(44100 * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                    for (int i = 0; i < n; i++)
                        w.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 8000));
                }
                string monoWav = Path.Combine(work, "mono.wav"), monoBtsnd = Path.Combine(work, "mono.btsnd");
                File.WriteAllBytes(monoWav, mono.ToArray());
                BootSoundTool.Create(monoWav, monoBtsnd, work);
                Check("btsnd via afconvert, trimmed to 6s", new FileInfo(monoBtsnd).Length == 8 + 48000 * 4 * 6, new FileInfo(monoBtsnd).Length + " bytes");
            }
        }

        private static void Gba(string work)
        {
            // Builds an archive shaped like a GBA base with the same library, then injects into it.
            string content = Path.Combine(work, "gba", "content"), source = Path.Combine(work, "gba", "source");
            Directory.CreateDirectory(Path.Combine(source, "system", "roms"));
            Directory.CreateDirectory(Path.Combine(source, "config"));
            Directory.CreateDirectory(content);
            var packer = new MArchivePacker(new ZlibCodec(), "MX8wgGEJ2+M47", 80);

            string oldRom = Path.Combine(source, "system", "roms", "TESTROM");
            File.WriteAllBytes(oldRom, Pattern(0x8000, 9));
            packer.CompressFile(oldRom);

            string prof = Path.Combine(source, "config", "title_prof.psb");
            using (FileStream stream = File.Create(prof))
                new PsbWriter(JToken.Parse("{\"m2epi\":{\"brightness\":0.8,\"other\":3},\"title\":\"x\"}"), null) { Version = 3 }.Write(stream);
            packer.CompressFile(prof);
            File.WriteAllText(Path.Combine(source, "config", "plain.txt"), "untouched");

            AllDataPacker.Build(source, Path.Combine(content, "alldata"), packer);
            File.Delete(Path.Combine(content, "alldata.psb"));

            byte[] newRom = Pattern(0x12345, 10);
            string romPath = Path.Combine(work, "gba", "new.gba");
            File.WriteAllBytes(romPath, newRom);
            GbaTool.Inject(content, romPath, removeDarkFilter: true);

            Check("gba archive rebuilt", File.Exists(Path.Combine(content, "alldata.psb.m")) && File.Exists(Path.Combine(content, "alldata.bin"))
                && !File.Exists(Path.Combine(content, "alldata.psb")) && !Directory.Exists(Path.Combine(content, "alldata.psb.m_extracted")));

            string verify = Path.Combine(work, "gba", "verify");
            AllDataPacker.UnpackFiles(Path.Combine(content, "alldata.psb.m"), verify, packer);
            packer.DecompressFile(Path.Combine(verify, "system", "roms", "TESTROM.m"));
            Check("gba rom replaced", File.ReadAllBytes(Path.Combine(verify, "system", "roms", "TESTROM")).SequenceEqual(newRom));

            packer.DecompressFile(Path.Combine(verify, "config", "title_prof.psb.m"));
            using (FileStream stream = File.OpenRead(Path.Combine(verify, "config", "title_prof.psb")))
            using (var reader = new PsbReader(stream))
                Check("gba dark filter removed", (double)reader.Root["m2epi"]["brightness"] == 1 && (int)reader.Root["m2epi"]["other"] == 3 && (string)reader.Root["title"] == "x");
            Check("gba other files kept", File.ReadAllText(Path.Combine(verify, "config", "plain.txt")) == "untouched");
        }

        private static void Pce(string work)
        {
            string rom = Path.Combine(work, "game.pce"), pkg = Path.Combine(work, "pce.pkg");
            byte[] data = Pattern(0x2000, 11);
            File.WriteAllBytes(rom, data);
            RomTools.BuildPcePkg(rom, pkg);
            byte[] result = File.ReadAllBytes(pkg);
            int body = BitConverter.ToInt32(result, 0);
            // 4 (config length) + 14 (name) + 32 + 64 + 64, then twice: 4 + "game.pce\0" + rom
            Check("pce.pkg layout", body == result.Length - 4 && body == 178 + 2 * (4 + 9 + data.Length) && result[4] == 160
                && Encoding.ASCII.GetString(result, 8, 13) == "pceconfig.bin" && Encoding.ASCII.GetString(result, 54, 8) == "game.pce");
        }

        private static void Pack(string work)
        {
            // A made-up title, packed and then decrypted again with the tool that decrypts real bases.
            const string key = "000102030405060708090a0b0c0d0e0f";
            string title = Path.Combine(work, "title"), packed = Path.Combine(work, "packed"), unpacked = Path.Combine(work, "unpacked");
            foreach (string part in new[] { "code", "content/sub", "meta" })
                Directory.CreateDirectory(Path.Combine(title, part));
            File.WriteAllText(Path.Combine(title, "code", "app.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><app type=\"complex\" access=\"777\"><version type=\"unsignedInt\" length=\"4\">16</version>" +
                "<os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version><title_id type=\"hexBinary\" length=\"8\">0005000212345678</title_id>" +
                "<title_version type=\"hexBinary\" length=\"2\">0000</title_version><sdk_version type=\"unsignedInt\" length=\"4\">21204</sdk_version>" +
                "<app_type type=\"hexBinary\" length=\"4\">80000000</app_type><group_id type=\"hexBinary\" length=\"4\">00001234</group_id>" +
                "<os_mask type=\"hexBinary\" length=\"32\">0000000000000000000000000000000000000000000000000000000000000000</os_mask>" +
                "<common_id type=\"hexBinary\" length=\"8\">0000000000000000</common_id></app>");
            File.WriteAllText(Path.Combine(title, "code", "cos.xml"), "<app/>");
            File.WriteAllBytes(Path.Combine(title, "code", "game.rpx"), Pattern(300000, 12));
            File.WriteAllBytes(Path.Combine(title, "content", "big.bin"), Pattern(700000, 13));
            File.WriteAllBytes(Path.Combine(title, "content", "sub", "small.bin"), Pattern(1234, 14));
            File.WriteAllText(Path.Combine(title, "meta", "meta.xml"), "<menu/>");
            File.WriteAllBytes(Path.Combine(title, "meta", "iconTex.tga"), Pattern(65580, 15));

            NusPacker.Pack(title, packed, key);
            Check("packer wrote a title", File.Exists(Path.Combine(packed, "title.tmd")) && File.Exists(Path.Combine(packed, "title.tik"))
                && File.Exists(Path.Combine(packed, "title.cert")) && Directory.GetFiles(packed, "*.app").Length >= 3,
                string.Join(" ", Directory.GetFiles(packed).Select(Path.GetFileName).OrderBy(n => n)));

            Directory.CreateDirectory(unpacked);
            TextWriter console = Console.Out;
            Console.SetOut(TextWriter.Null);
            try { CSharpDecrypt.CSharpDecrypt.Decrypt(new[] { key, packed, unpacked }); }
            finally { Console.SetOut(console); }

            string[] files = Directory.GetFiles(title, "*", SearchOption.AllDirectories);
            int same = files.Count(f =>
            {
                string other = Path.Combine(unpacked, Path.GetRelativePath(title, f));
                return File.Exists(other) && File.ReadAllBytes(other).SequenceEqual(File.ReadAllBytes(f));
            });
            Check("packed title decrypts back to the same files", same == files.Length, $"{same}/{files.Length} identical");
        }
    }
}
