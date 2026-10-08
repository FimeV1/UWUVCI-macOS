using System;
using System.IO;
using System.Linq;
using GMWare.M2.MArchive;
using GMWare.M2.Psb;
using Newtonsoft.Json.Linq;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    /// <summary>
    /// GBA Virtual Console archive handling (alldata.psb.m + alldata.bin). Replaces psb.exe and MArchiveBatchTool.exe,
    /// using the same library MArchiveBatchTool is built on.
    /// </summary>
    public static class GbaTool
    {
        private const string Seed = "MX8wgGEJ2+M47";
        private const int KeyLength = 80;

        private static MArchivePacker Packer() => new MArchivePacker(new ZlibCodec(), Seed, KeyLength);

        /// <summary>Replaces the ROM inside the base's archive, optionally turning the dark filter off.</summary>
        public static void Inject(string contentDirectory, string romPath, bool removeDarkFilter)
        {
            string psbM = Path.Combine(contentDirectory, "alldata.psb.m");
            string extracted = Path.Combine(contentDirectory, "alldata.psb.m_extracted");
            if (!File.Exists(psbM) || !File.Exists(Path.Combine(contentDirectory, "alldata.bin")))
                throw new FileNotFoundException("The base is missing content/alldata.psb.m or alldata.bin, it is not a GBA base.");

            if (Directory.Exists(extracted))
                Directory.Delete(extracted, true);

            MArchivePacker packer = Packer();
            try
            {
                AllDataPacker.UnpackFiles(psbM, extracted, packer);

                string romEntry = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories)
                    .Where(f => f.Replace('\\', '/').Contains("system/roms/"))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (romEntry == null)
                    throw new InvalidDataException("Could not find the ROM inside the GBA base (no system/roms entry).");

                // Entries are stored compressed and obfuscated with a key derived from their own file name,
                // so the new ROM has to be packed under exactly the name of the one it replaces.
                if (!romEntry.EndsWith(".m", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unexpected ROM entry name in the GBA base: {Path.GetFileName(romEntry)}");
                string rawRom = romEntry.Substring(0, romEntry.Length - 2);
                File.Delete(romEntry);
                File.Copy(romPath, rawRom, true);
                packer.CompressFile(rawRom);
                Logger.Log($"Replaced {Path.GetFileName(romEntry)} in the GBA archive.");

                if (removeDarkFilter)
                    RemoveDarkFilter(extracted, packer);

                AllDataPacker.Build(extracted, Path.Combine(contentDirectory, "alldata"), packer);
            }
            finally
            {
                if (Directory.Exists(extracted))
                    Directory.Delete(extracted, true);
                string plainPsb = Path.Combine(contentDirectory, "alldata.psb");
                if (File.Exists(plainPsb))
                    File.Delete(plainPsb);
            }
        }

        private static void RemoveDarkFilter(string extracted, MArchivePacker packer)
        {
            string profM = Directory.GetFiles(extracted, "title_prof.psb.m", SearchOption.AllDirectories).FirstOrDefault();
            if (profM == null)
            {
                Logger.Log("title_prof.psb.m not found in the GBA base, leaving the dark filter as it is.");
                return;
            }

            packer.DecompressFile(profM);
            string prof = profM.Substring(0, profM.Length - 2);

            JToken root;
            ushort version;
            PsbFlags flags;
            using (FileStream stream = File.OpenRead(prof))
            using (var reader = new PsbReader(stream))
            {
                root = reader.Root.DeepClone();
                version = reader.Version;
                flags = reader.Flags;
            }

            root["m2epi"]["brightness"] = 1;

            using (FileStream stream = File.Create(prof))
                new PsbWriter(root, null) { Version = version, Flags = flags }.Write(stream);
            packer.CompressFile(prof);
        }
    }
}
