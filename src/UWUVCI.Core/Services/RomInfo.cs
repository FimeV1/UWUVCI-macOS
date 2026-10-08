using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameBaseClassLibrary;

namespace UWUVCI.Core.Services
{
    /// <summary>What can be read out of a ROM: its internal name and the ids used to look up artwork.</summary>
    public sealed class RomDetails
    {
        /// <summary>Internal game name (Wii / GameCube only), or "".</summary>
        public string InternalName = "";
        /// <summary>Candidate folders in the UWUVCI-IMAGES repository, e.g. "gba/BPEE".</summary>
        public List<string> RepoIds = new List<string>();
    }

    public sealed class RepoArtwork
    {
        public string RepoId;
        public string IconUrl, TvUrl;
        public string IniUrl, BootSoundUrl;
    }

    public static class RomInfo
    {
        public const string ImagesUrl = "https://raw.githubusercontent.com/UWUVCI-PRIME/UWUVCI-IMAGES/master/";

        private static readonly Regex NotAlphanumeric = new Regex("[^a-zA-Z0-9 -]");

        /// <summary>Reads the ids of a ROM. Throws when the file is too short or not a ROM of that console.</summary>
        public static RomDetails Read(string rom, GameConsoles console, bool turboCd = false)
        {
            var details = new RomDetails();
            switch (console)
            {
                case GameConsoles.GBA:
                    AddRegionVariants(details, "gba/", ReadAscii(rom, 0xAC, 4));
                    break;
                case GameConsoles.NDS:
                    AddRegionVariants(details, "nds/", ReadAscii(rom, 0xC, 4));
                    break;
                case GameConsoles.N64:
                {
                    string id = ReadAscii(rom, 0x3A, 6);
                    details.RepoIds.Add("n64/" + id);
                    details.RepoIds.Add("n64/" + new string(new[] { id[0], id[2], id[1], id[3] }));
                    break;
                }
                case GameConsoles.SNES:
                    details.RepoIds.Add("snes/" + FakeSnesProdcode(rom));
                    break;
                case GameConsoles.NES:
                    details.RepoIds.Add("nes/" + CodeOfNumbers(Md5Digits(ReadBytes(rom, 0x8000, 0xB0))));
                    break;
                case GameConsoles.MSX:
                    details.RepoIds.Add("msx/" + CodeOfNumbers(Md5Digits(ReadBytes(rom, 0, 0x210))) + "SX");
                    break;
                case GameConsoles.TG16:
                    if (!turboCd && File.Exists(rom))
                        details.RepoIds.Add("tg16/" + CodeOfNumbers(Md5Digits(ReadBytes(rom, 0, 0x210))) + "TG");
                    break;
                case GameConsoles.WII:
                case GameConsoles.GCN:
                    ReadDisc(rom, console == GameConsoles.GCN ? "gcn/" : "wii/", details);
                    break;
            }
            return details;
        }

        private static byte[] ReadBytes(string path, long offset, int count)
        {
            var buffer = new byte[count];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            fs.Seek(offset, SeekOrigin.Begin);
            fs.Read(buffer, 0, count);
            return buffer;
        }

        private static string ReadAscii(string path, long offset, int count) =>
            NotAlphanumeric.Replace(Encoding.ASCII.GetString(ReadBytes(path, offset, count)), "");

        private static void AddRegionVariants(RomDetails details, string system, string id)
        {
            details.RepoIds.Add(system + id);
            foreach (char region in "EPJ")
                details.RepoIds.Add(system + id.Substring(0, 3) + region);
        }

        private static string ReadCString(BinaryReader reader, long position)
        {
            reader.BaseStream.Position = position;
            var sb = new StringBuilder();
            for (byte b; (b = reader.ReadByte()) != 0 && sb.Length < 128;)
                sb.Append((char)b);
            return sb.ToString();
        }

        private static void ReadDisc(string rom, string system, RomDetails details)
        {
            if (!File.Exists(rom) || rom.EndsWith(".dol", StringComparison.OrdinalIgnoreCase) || rom.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
                return;

            using var reader = new BinaryReader(File.OpenRead(rom));
            // A WBFS file is a 0x200 byte header followed by the disc.
            long start = new FileInfo(rom).Extension.ToLower().Contains("wbfs") ? 0x200 : 0;
            details.InternalName = ReadCString(reader, start + 0x20);
            string id = ReadCString(reader, start);
            if (id.Length < 6)
                throw new InvalidDataException("The file does not start with a disc id.");

            details.RepoIds.Add(system + id);
            foreach (char region in "EPJ")
                details.RepoIds.Add(system + id.Substring(0, 3) + region + id.Substring(4, 2));
        }

        private static string FakeSnesProdcode(string path)
        {
            byte[] name = Array.Empty<byte>();
            string id = ReadAscii(path, 0x7FB2, 4);
            if (id.Length < 4)
            {
                name = ReadBytes(path, 0xFFC0, 21);
                id = NotAlphanumeric.Replace(Encoding.ASCII.GetString(name), "");
            }
            if (id.Length < 4)
                name = ReadBytes(path, 0x7FC0, 21);

            string gameName = NotAlphanumeric.Replace(Encoding.ASCII.GetString(name), "");
            using var md5 = MD5.Create();
            return CodeOfNumbers(Digits(md5.ComputeHash(Encoding.UTF8.GetBytes(gameName))));
        }

        private static string Md5Digits(byte[] data)
        {
            using var md5 = MD5.Create();
            return Digits(md5.ComputeHash(data));
        }

        /// <summary>The first nine decimal digits of the hash's hex text, padded with zeros.</summary>
        private static string Digits(byte[] hash)
        {
            string hex = string.Concat(hash.Select(b => b.ToString("x2")));
            string digits = new string(hex.Where(char.IsDigit).ToArray()).PadRight(10, '0');
            return digits.Substring(0, 9);
        }

        // Turns the digits into the four letter fake product code the image repository is indexed by.
        // The arithmetic (adding character codes, not digits) is exactly what the Windows app does.
        private static string CodeOfNumbers(string nineDigits)
        {
            string ts = int.Parse(nineDigits).ToString();
            int n1 = ts[0] + ts[1];
            int n2 = ts[2] + ts[3];
            int n3 = ts[4] + ts[5];
            int n4 = ts.Length > 7 ? ts[6] + ts[7] : ts[6];

            const string letters = "abcdefghijklmnopqrstuvwxyz";
            int Wrap(int n)
            {
                while (n > 23)
                    n -= 23;
                return n;
            }
            return new string(new[] { letters[Wrap(n1)], letters[Wrap(n2)], letters[Wrap(n3)], letters[Wrap(n4)] }).ToUpper();
        }

        /// <summary>Looks for artwork (and an N64 INI / boot sound) for the ROM in the community image repository.</summary>
        public static async Task<RepoArtwork> FindArtworkAsync(RomDetails details, GameConsoles console, CancellationToken cancel = default)
        {
            if (details.RepoIds.Count == 0)
                return null;

            var artwork = new RepoArtwork();
            foreach (string extension in new[] { "png", "jpg", "jpeg", "tga" })
            {
                foreach (string id in details.RepoIds)
                {
                    string url = $"{ImagesUrl}{id}/iconTex.{extension}";
                    if (await ToolManager.RemoteFileExistsAsync(url, cancel).ConfigureAwait(false))
                    {
                        artwork.RepoId = id;
                        artwork.IconUrl = url;
                        artwork.TvUrl = url.Replace("iconTex", "bootTvTex");
                        break;
                    }
                }
                if (artwork.IconUrl != null)
                    break;
            }

            foreach (string id in details.RepoIds)
            {
                if (console == GameConsoles.N64 && artwork.IniUrl == null && await ToolManager.RemoteFileExistsAsync($"{ImagesUrl}{id}/game.ini", cancel).ConfigureAwait(false))
                    artwork.IniUrl = $"{ImagesUrl}{id}/game.ini";
                if (artwork.BootSoundUrl == null && await ToolManager.RemoteFileExistsAsync($"{ImagesUrl}{id}/BootSound.btsnd", cancel).ConfigureAwait(false))
                    artwork.BootSoundUrl = $"{ImagesUrl}{id}/BootSound.btsnd";
            }

            return artwork.IconUrl == null && artwork.IniUrl == null && artwork.BootSoundUrl == null ? null : artwork;
        }

        /// <summary>Downloads a repository file into the app's cache and returns the local path.</summary>
        public static async Task<string> DownloadAsync(string url, string localName, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(AppPaths.Repo);
            string path = Path.Combine(AppPaths.Repo, localName);
            await ToolManager.DownloadFileAsync(url, path, cancel).ConfigureAwait(false);
            return path;
        }
    }
}
