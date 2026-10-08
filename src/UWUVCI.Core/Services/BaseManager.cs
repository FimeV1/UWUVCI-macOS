using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBaseClassLibrary;
using UWUVCI_AIO_WPF.Helpers;
using UWUVCI_AIO_WPF.Models;
using WiiUDownloaderLibrary;
using WiiUDownloaderLibrary.Models;

namespace UWUVCI.Core.Services
{
    /// <summary>Base game lists (.vcb), the title keys entered for them (.vck), the common key, and base downloads.</summary>
    public static class BaseManager
    {
        // The "Net6" lists carry key hashes computed with GetDeterministicHashCode; the top-level ones depend on the
        // .NET Framework string hash and can't be checked outside Windows.
        public const string VcbUrl = "https://github.com/Hotbrawl20/UWUVCI-VCB/raw/master/Net6/";

        public static readonly GameConsoles[] ListedConsoles =
        {
            GameConsoles.NDS, GameConsoles.NES, GameConsoles.N64, GameConsoles.SNES,
            GameConsoles.GBA, GameConsoles.TG16, GameConsoles.MSX, GameConsoles.WII,
        };

        private const int CommonKeyHash = -485504051;

        /// <summary>GameCube injects run on Wii bases.</summary>
        private static GameConsoles ListConsole(GameConsoles console) => console == GameConsoles.GCN ? GameConsoles.WII : console;

        private static string VcbFile(GameConsoles console) => Path.Combine(AppPaths.Bases, "bases.vcb" + ListConsole(console).ToString().ToLower());
        private static string KeyFile(GameConsoles console) => Path.Combine(AppPaths.Keys, ListConsole(console).ToString().ToLower() + ".vck");

        public static List<GameConsoles> MissingLists() => ListedConsoles.Where(c => !File.Exists(VcbFile(c))).ToList();

        public static async Task DownloadListsAsync(bool all, IProgress<(int, string)> progress = null, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(AppPaths.Bases);
            List<GameConsoles> wanted = all ? ListedConsoles.ToList() : MissingLists();
            for (int i = 0; i < wanted.Count; i++)
            {
                string name = Path.GetFileName(VcbFile(wanted[i]));
                progress?.Report((i * 100 / wanted.Count, name));
                string partial = VcbFile(wanted[i]) + ".part";
                await ToolManager.DownloadFileAsync(VcbUrl + name, partial, cancel).ConfigureAwait(false);
                File.Move(partial, VcbFile(wanted[i]), true);
            }
            progress?.Report((100, ""));
        }

        /// <summary>The downloadable bases of a console (without the "Custom" entry).</summary>
        public static List<GameBases> GetBases(GameConsoles console)
        {
            string file = VcbFile(console);
            if (!File.Exists(file))
                return new List<GameBases>();
            try
            {
                return LegacySerializer.Read<List<GameBases>>(file);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not read {file}: {ex.Message}");
                return new List<GameBases>();
            }
        }

        public static GameBases CustomBase() => new GameBases { Name = "Custom", Region = Regions.EU };

        public static string DisplayName(GameBases b) => b.Name == "Custom" ? b.Name : $"{b.Name} {b.Region}";

        public static string BaseFolder(GameBases b) =>
            Path.Combine(JsonSettingsManager.Settings.BasePath, $"{b.Name.Replace(":", "")} [{b.Region}]");

        public static bool IsDownloaded(GameBases b)
        {
            string folder = BaseFolder(b);
            return Directory.Exists(Path.Combine(folder, "code")) && Directory.Exists(Path.Combine(folder, "content")) && Directory.Exists(Path.Combine(folder, "meta"));
        }

        // ---- keys ---------------------------------------------------------------------------------------------------

        public static int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        private static string Normalize(string key) => (key ?? "").Trim().ToLowerInvariant();

        public static bool IsCommonKeySet => GetDeterministicHashCode(Normalize(JsonSettingsManager.Settings.Ckey)) == CommonKeyHash;

        /// <summary>Stores the Wii U common key when it is the right one.</summary>
        public static bool TrySetCommonKey(string key)
        {
            key = Normalize(key);
            if (GetDeterministicHashCode(key) != CommonKeyHash)
                return false;
            JsonSettingsManager.Settings.Ckey = key;
            JsonSettingsManager.SaveSettings();
            return true;
        }

        private static List<TKeys> ReadKeys(GameConsoles console)
        {
            string file = KeyFile(console);
            if (!File.Exists(file))
                return new List<TKeys>();
            try
            {
                return LegacySerializer.Read<List<TKeys>>(file);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not read {file}: {ex.Message}");
                return new List<TKeys>();
            }
        }

        private static bool Same(GameBases a, GameBases b) => a != null && b != null && a.Name == b.Name && a.Region == b.Region;

        public static string GetTitleKey(GameConsoles console, GameBases b) =>
            ReadKeys(console).FirstOrDefault(k => Same(k.Base, b) && !string.IsNullOrEmpty(k.Tkey))?.Tkey;

        public static bool IsTitleKeySet(GameConsoles console, GameBases b) => GetTitleKey(console, b) != null;

        /// <summary>Stores the title key of a base when it is the right one.</summary>
        public static bool TrySetTitleKey(GameConsoles console, GameBases b, string key)
        {
            key = Normalize(key);
            if (b.KeyHash != GetDeterministicHashCode(key))
                return false;

            List<TKeys> keys = ReadKeys(console);
            TKeys entry = keys.FirstOrDefault(k => Same(k.Base, b));
            if (entry == null)
                keys.Add(entry = new TKeys { Base = b });
            entry.Tkey = key;
            LegacySerializer.Write(KeyFile(console), keys);
            return true;
        }

        public static void ResetTitleKeys()
        {
            if (Directory.Exists(AppPaths.Keys))
                foreach (string file in Directory.GetFiles(AppPaths.Keys, "*.vck"))
                    File.Delete(file);
        }

        // ---- downloads ----------------------------------------------------------------------------------------------

        /// <summary>Downloads a base from Nintendo's CDN and decrypts it into the base folder.</summary>
        public static async Task DownloadBaseAsync(GameConsoles console, GameBases b, IProgress<(int, string)> progress = null)
        {
            string titleKey = GetTitleKey(console, b) ?? throw new InvalidOperationException("The Title Key for this base has not been entered.");
            if (!IsCommonKeySet)
                throw new InvalidOperationException("The Wii U Common Key has not been entered.");

            string download = Path.Combine(AppPaths.Temp, "download");
            if (Directory.Exists(AppPaths.Temp))
                Directory.Delete(AppPaths.Temp, true);
            Directory.CreateDirectory(download);

            try
            {
                progress?.Report((0, "Downloading..."));
                var downloader = new Downloader(null, null);
                var reporter = new Progress<double>(p => progress?.Report(((int)Math.Clamp(p > 1 ? p * 0.9 : p * 90, 0, 90), "Downloading...")));
                await downloader.DownloadAsync(new TitleData(b.Tid, titleKey), download, reporter).ConfigureAwait(false);

                progress?.Report((92, "Decrypting..."));
                string target = BaseFolder(b);
                await Task.Run(() => Decrypt(Path.Combine(download, b.Tid), target)).ConfigureAwait(false);

                if (ListConsole(console) == GameConsoles.WII)
                    foreach (string nfs in Directory.GetFiles(Path.Combine(target, "content"), "*.nfs"))
                        File.Delete(nfs);

                if (!IsDownloaded(b))
                    throw new IOException("The base did not download completely.");
                progress?.Report((100, ""));
            }
            finally
            {
                if (Directory.Exists(AppPaths.Temp))
                    Directory.Delete(AppPaths.Temp, true);
            }
        }

        private static readonly object decryptGate = new object();

        private static void Decrypt(string nusFolder, string target)
        {
            if (!File.Exists(Path.Combine(nusFolder, "title.tmd")) || !File.Exists(Path.Combine(nusFolder, "title.tik")))
                throw new FileNotFoundException("The folder does not contain a title in NUS format (title.tmd / title.tik).");

            Directory.CreateDirectory(target);
            lock (decryptGate)
            {
                TextWriter console = Console.Out;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    CSharpDecrypt.CSharpDecrypt.Decrypt(new[] { JsonSettingsManager.Settings.Ckey, nusFolder, target });
                }
                finally
                {
                    Console.SetOut(console);
                }
            }
        }

        // ---- custom bases -------------------------------------------------------------------------------------------

        public enum CustomBaseKind { Invalid, Loadiine, Nus }

        public static CustomBaseKind InspectCustomBase(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return CustomBaseKind.Invalid;
            if (Directory.Exists(Path.Combine(folder, "code")) && Directory.Exists(Path.Combine(folder, "content")) && Directory.Exists(Path.Combine(folder, "meta")))
                return CustomBaseKind.Loadiine;
            if (File.Exists(Path.Combine(folder, "title.tmd")) && File.Exists(Path.Combine(folder, "title.tik")))
                return CustomBaseKind.Nus;
            return CustomBaseKind.Invalid;
        }

        /// <summary>Decrypts a custom base in NUS format so it can be injected into. Returns the decrypted folder.</summary>
        public static string ExtractCustomBase(string nusFolder, GameConsoles console)
        {
            if (!IsCommonKeySet)
                throw new InvalidOperationException("The Wii U Common Key has not been entered.");

            string root = Path.Combine(JsonSettingsManager.Settings.BasePath, "CustomBases");
            string output = Path.Combine(root, $"[{console}] Custom");
            for (int i = 0; Directory.Exists(output); i++)
                output = Path.Combine(root, $"[{console}] Custom_{i}");

            Decrypt(nusFolder, output);
            return output;
        }
    }
}
