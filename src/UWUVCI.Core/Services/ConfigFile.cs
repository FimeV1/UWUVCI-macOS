using System;
using System.IO;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Models;

namespace UWUVCI.Core.Services
{
    /// <summary>
    /// .uwuvci inject configs: the settings of an inject with its images, INI and boot sound embedded, in the same
    /// format the Windows app reads and writes.
    /// </summary>
    public static class ConfigFile
    {
        public static GameConfig Read(string path) => LegacySerializer.Read<GameConfig>(path);

        /// <summary>Writes a copy of the config with every referenced file embedded. Returns the file written.</summary>
        public static string Export(GameConfig config, string bootSoundPath, string folder = null)
        {
            GameConfig copy = config.Clone();
            copy.TGAIco = Embed(config.TGAIco);
            copy.TGADrc = Embed(config.TGADrc);
            copy.TGATv = Embed(config.TGATv);
            copy.TGALog = Embed(config.TGALog);

            copy.N64Stuff = new N64Conf
            {
                DarkFilter = config.N64Stuff.DarkFilter,
                WideScreen = config.N64Stuff.WideScreen,
                INIPath = config.N64Stuff.INIPath,
                INIBin = config.N64Stuff.INIBin,
            };
            if (copy.N64Stuff.INIBin == null && File.Exists(config.N64Stuff.INIPath ?? ""))
            {
                copy.N64Stuff.INIBin = File.ReadAllBytes(config.N64Stuff.INIPath);
                copy.N64Stuff.INIPath = "Added via Config";
            }

            if (File.Exists(bootSoundPath ?? ""))
            {
                copy.bootsound = File.ReadAllBytes(bootSoundPath);
                copy.extension = Path.GetExtension(bootSoundPath).Replace(".", "");
            }

            if (string.IsNullOrEmpty(copy.GameName))
                copy.GameName = "NoName";

            folder ??= AppPaths.Configs;
            Directory.CreateDirectory(folder);
            string name = copy.GameName;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), string.Empty);
            name = name.Replace("/", "").Replace(":", "").Replace("|", " ");

            string output = Path.Combine(folder, $"[{copy.Console}]{name}.uwuvci");
            for (int i = 1; File.Exists(output); i++)
                output = Path.Combine(folder, $"[{copy.Console}]{name}_{i}.uwuvci");

            LegacySerializer.Write(output, copy);
            return output;
        }

        private static PNGTGA Embed(PNGTGA image)
        {
            var copy = new PNGTGA { ImgPath = image.ImgPath, ImgBin = image.ImgBin, extension = image.extension };
            if (copy.ImgBin == null && File.Exists(image.ImgPath ?? ""))
            {
                copy.ImgBin = File.ReadAllBytes(image.ImgPath);
                copy.extension = Path.GetExtension(image.ImgPath).Replace(".", "");
                copy.ImgPath = "Added via Config";
            }
            return copy;
        }

        /// <summary>Writes the boot sound embedded in a config to disk and returns its path, or null when there is none.</summary>
        public static string ExtractBootSound(GameConfig config)
        {
            if (config.bootsound == null || config.bootsound.Length == 0)
                return null;
            Directory.CreateDirectory(AppPaths.Repo);
            string path = Path.Combine(AppPaths.Repo, "configBootSound." + (string.IsNullOrEmpty(config.extension) ? "btsnd" : config.extension));
            File.WriteAllBytes(path, config.bootsound);
            return path;
        }
    }
}
