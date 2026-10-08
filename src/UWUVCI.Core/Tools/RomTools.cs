using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    /// <summary>Managed replacements for the small Windows-only command line tools the injector shells out to.</summary>
    public static class RomTools
    {
        // ---- RetroInject.exe (port of Morilli's Retroinject_C) -----------------------------------------------------

        private static int VcSizeLookup(byte key) => key switch
        {
            0 => 0x8000,
            1 => 0x10000,
            2 => 0x20000,
            0xC => 0x20000,
            4 => 0x40000,
            6 => 0x60000,
            8 => 0x80000,
            0x10 => 0x100000,
            0x20 => 0x200000,
            0x30 => 0x300000,
            0x40 => 0x400000,
            _ => throw new InvalidDataException("Unknown ROM size key in the base RPX."),
        };

        /// <summary>Injects a NES/SNES ROM into a decompressed Virtual Console RPX. Throws "retro" when the ROM does not fit.</summary>
        public static void RetroInject(string elfPath, string romPath, string outputPath)
        {
            byte[] elf = File.ReadAllBytes(elfPath);
            byte[] rom = File.ReadAllBytes(romPath);
            byte[] nesMagic = { 0x4E, 0x45, 0x53, 0x1A };

            int wup = -1;
            for (int i = 0; i + 20 <= elf.Length; i += 4)
                if (elf[i] == 0x57 && elf[i + 1] == 0x55 && elf[i + 2] == 0x50 && elf[i + 3] == 0x2D)
                {
                    wup = i;
                    break;
                }
            if (wup < 22)
                throw new InvalidDataException("Couldn't find WUP magic code in the base RPX.");

            bool romIsNes = rom.Length >= 4 && rom.AsSpan(0, 4).SequenceEqual(nesMagic);
            var vcMagic = elf.AsSpan(wup + 16, 4);
            bool vcIsNes = vcMagic.SequenceEqual(nesMagic) || vcMagic.SequenceEqual(new byte[] { 0x4E, 0x45, 0x53, 0x00 });
            byte sizeKey = elf[vcIsNes ? wup - 14 : wup - 22];
            int inject = vcIsNes ? wup + 16 : wup + 12;
            int vcSize = VcSizeLookup(sizeKey) + (vcIsNes ? 16 : 0);

            if (rom.Length > vcSize)
                throw new Exception("retro");
            if (romIsNes != vcIsNes)
                throw new Exception("The base and the ROM need to be of the same type (NES base for NES ROMs, SNES base for SNES ROMs).");

            Buffer.BlockCopy(rom, 0, elf, inject, rom.Length);
            File.WriteAllBytes(outputPath, elf);
        }

        // ---- GetExtTypePatcher.exe (port of FIX94's GetExtType Patcher v1.1, MIT) ----------------------------------

        private struct FuncPattern
        {
            public uint Length, Loads, Stores, FCalls, Branch, Moves;

            public bool Matches(uint length, uint loads, uint stores, uint fcalls, uint branch, uint moves) =>
                Length == length && Loads == loads && Stores == stores && FCalls == fcalls && Branch == branch && Moves == moves;
        }

        private static FuncPattern MeasureFunction(byte[] data, int start, int maxLength)
        {
            var p = new FuncPattern();
            uint i;
            for (i = 0; i < maxLength && start + i + 4 <= data.Length; i += 4)
            {
                uint word = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)(start + i)));

                if ((word & 0xFC000003) == 0x48000001) p.FCalls++;

                if ((word & 0xFC000003) == 0x48000000) p.Branch++;
                if ((word & 0xFFFF0000) == 0x40800000) p.Branch++;
                if ((word & 0xFFFF0000) == 0x41800000) p.Branch++;
                if ((word & 0xFFFF0000) == 0x40810000) p.Branch++;
                if ((word & 0xFFFF0000) == 0x41820000) p.Branch++;

                if ((word & 0xFC000000) == 0x80000000) p.Loads++;
                if ((word & 0xFF000000) == 0x38000000) p.Loads++;
                if ((word & 0xFF000000) == 0x3C000000) p.Loads++;

                if ((word & 0xFC000000) == 0x90000000) p.Stores++;
                if ((word & 0xFC000000) == 0x94000000) p.Stores++;

                if ((word & 0xFF000000) == 0x7C000000) p.Moves++;
                if (word == 0x4E800020)
                    break;
            }
            p.Length = i;
            return p;
        }

        /// <summary>Patches a Wii main.dol so the game always sees a Classic Controller. Returns false when nothing matched.</summary>
        public static bool PatchGetExtType(string dolPath)
        {
            byte[] buf = File.ReadAllBytes(dolPath);
            if (buf.Length < 0x100 || BinaryPrimitives.ReadUInt32BigEndian(buf) != 0x00000100)
            {
                Logger.Log("GetExtType patcher: input is not a dol file.");
                return false;
            }

            const uint li_r0_1 = 0x38000001;
            for (int i = 0x100; i + 4 <= buf.Length; i += 4)
            {
                if (BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(i)) != 0x4E800020)
                    continue;
                i += 4;
                FuncPattern func = MeasureFunction(buf, i, 0x2000);

                int first, second;
                if (func.Matches(0xD4, 18, 4, 3, 7, 2) || func.Matches(0xFC, 19, 4, 3, 9, 2))
                {
                    first = i + 0x40;
                    second = i + 0x48;
                }
                else if (func.Matches(0x26C, 59, 2, 16, 12, 10))
                {
                    first = i + 0x38;
                    second = i + 0x44;
                }
                else
                    continue;

                if (second + 4 > buf.Length)
                    continue;
                // Both instructions must be "lbz r0, x(rX)" before they are replaced with "li r0, 1".
                if ((BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(first)) & 0xFFE00000) != 0x88000000 ||
                    (BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(second)) & 0xFFE00000) != 0x88000000)
                    continue;

                BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(first), li_r0_1);
                BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(second), li_r0_1);
                File.WriteAllBytes(dolPath, buf);
                Logger.Log("GetExtType patched.");
                return true;
            }

            Logger.Log("GetExtType patcher: nothing found to patch.");
            return false;
        }

        // ---- N64Converter.exe ---------------------------------------------------------------------------------------

        /// <summary>Writes the ROM to <paramref name="outputPath"/> in big-endian (.z64) byte order, whatever order it came in.</summary>
        public static void ConvertN64(string inputPath, string outputPath)
        {
            byte[] rom = File.ReadAllBytes(inputPath);
            if (rom.Length >= 4)
            {
                uint magic = BinaryPrimitives.ReadUInt32BigEndian(rom);
                if (magic == 0x37804012)        // .v64, byte-swapped
                {
                    for (int i = 0; i + 1 < rom.Length; i += 2)
                        (rom[i], rom[i + 1]) = (rom[i + 1], rom[i]);
                }
                else if (magic == 0x40123780)   // .n64, little-endian
                {
                    for (int i = 0; i + 3 < rom.Length; i += 4)
                    {
                        (rom[i], rom[i + 3]) = (rom[i + 3], rom[i]);
                        (rom[i + 1], rom[i + 2]) = (rom[i + 2], rom[i + 1]);
                    }
                }
                else if (magic != 0x80371240)
                    Logger.Log($"N64 ROM has an unknown header ({magic:X8}); injecting it unchanged.");
            }
            File.WriteAllBytes(outputPath, rom);
        }

        // ---- BuildPcePkg.exe / BuildTurboCdPcePkg.exe ---------------------------------------------------------------

        private static byte[] LittleEndian(int length) => BitConverter.GetBytes(length);

        private static readonly byte[] PceConfigName = Encoding.ASCII.GetBytes("pceconfig.bin\0");

        /// <summary>Builds pce.pkg for a TurboGrafx-16 HuCard ROM.</summary>
        public static void BuildPcePkg(string romPath, string outputPkg)
        {
            byte[] rom = File.ReadAllBytes(romPath);
            byte[] name = Encoding.ASCII.GetBytes(Path.GetFileName(romPath));
            if (name.Length > 64)
                throw new Exception("The ROM file name is too long for a TurboGrafx inject. Shorten it to 64 characters or less.");

            var body = new List<byte>();
            body.AddRange(new byte[] { 160, 0, 0, 0 });
            body.AddRange(PceConfigName);
            body.AddRange(new byte[32]);
            for (int i = 0; i < 2; i++)
            {
                body.AddRange(name);
                body.AddRange(new byte[64 - name.Length]);
            }
            for (int i = 0; i < 2; i++)
            {
                body.AddRange(LittleEndian(rom.Length));
                body.AddRange(name);
                body.Add(0);
                body.AddRange(rom);
            }
            WritePkg(outputPkg, body);
        }

        /// <summary>Builds pce.pkg for a TurboGrafx-CD game folder (.hcd + .ogg + .bin).</summary>
        public static void BuildTurboCdPcePkg(string gameFolder, string outputPkg)
        {
            string[] hcds = Directory.GetFiles(gameFolder, "*.hcd");
            if (hcds.Length == 0 || Directory.GetFiles(gameFolder, "*.ogg").Length == 0 || Directory.GetFiles(gameFolder, "*.bin").Length == 0)
                throw new Exception("The TurboGrafx-CD folder needs to contain the .hcd, .ogg and .bin files of the game.");
            if (hcds.Length != 1)
                throw new Exception("The TurboGrafx-CD folder contains more than one .hcd file, there can be only one.");

            // The Windows tool is always run on a copy of the folder called "test", and that name ends up inside the pkg.
            const string prefix = "test/";
            Encoding latin1 = Encoding.Latin1;
            string hcdName = Path.GetFileName(hcds[0]);
            byte[] hcdEntry = latin1.GetBytes(prefix + hcdName);
            if (hcdEntry.Length > 64)
                throw new Exception("The .hcd file name is too long. Shorten it to 59 characters or less.");

            var body = new List<byte>();
            body.AddRange(new byte[] { 160, 0, 0, 0 });
            body.AddRange(PceConfigName);
            body.AddRange(new byte[] { 1, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            for (int i = 0; i < 2; i++)
            {
                body.AddRange(hcdEntry);
                body.AddRange(new byte[64 - hcdEntry.Length]);
            }

            void AddFile(string fileName)
            {
                byte[] data = File.ReadAllBytes(Path.Combine(gameFolder, fileName));
                body.AddRange(LittleEndian(data.Length));
                body.AddRange(latin1.GetBytes(prefix + fileName));
                body.Add(0);
                body.AddRange(data);
            }

            AddFile(hcdName);
            foreach (string line in File.ReadAllText(hcds[0]).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(',');
                if (parts.Length > 2 && File.Exists(Path.Combine(gameFolder, parts[2])))
                    AddFile(parts[2]);
            }
            WritePkg(outputPkg, body);
        }

        private static void WritePkg(string outputPkg, List<byte> body)
        {
            using var stream = new FileStream(outputPkg, FileMode.Create, FileAccess.Write);
            stream.Write(LittleEndian(body.Count), 0, 4);
            stream.Write(body.ToArray(), 0, body.Count);
        }
    }
}
