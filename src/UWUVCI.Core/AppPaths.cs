using System;
using System.IO;
using System.Runtime.InteropServices;

namespace UWUVCI.Core
{
    /// <summary>
    /// Every on-disk location the app uses. The Windows app keeps all of this next to the exe (bin\Tools, bin\bases, ...);
    /// an app bundle is read-only, so on macOS it lives under ~/Library/Application Support/UWUVCI instead.
    /// </summary>
    public static class AppPaths
    {
        public static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static string DataRoot { get; private set; } = ResolveDataRoot();

        public static string Bin => Path.Combine(DataRoot, "bin");
        public static string Tools => Path.Combine(Bin, "Tools");
        public static string Bases => Path.Combine(Bin, "bases");
        public static string Keys => Path.Combine(Bin, "keys");
        public static string Temp => Path.Combine(Bin, "temp");
        public static string Repo => Path.Combine(Bin, "repo");
        public static string CreatedImages => Path.Combine(Bin, "createdIMG");
        public static string Configs => Path.Combine(DataRoot, "configs");
        public static string Logs => Path.Combine(DataRoot, "Logs");
        public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

        public static string DefaultBasePath => Path.Combine(Bin, "BaseGames");
        public static string DefaultOutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "UWUVCI", "InjectedGames");

        /// <summary>Folder holding files shipped inside the app (native tools, frames).</summary>
        public static string Bundled => AppContext.BaseDirectory;

        private static string ResolveDataRoot()
        {
            string over = Environment.GetEnvironmentVariable("UWUVCI_HOME");
            if (!string.IsNullOrWhiteSpace(over))
                return Path.GetFullPath(over);

            if (IsMac)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "UWUVCI");

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UWUVCI-Mac");
        }

        public static void SetDataRoot(string path) => DataRoot = Path.GetFullPath(path);

        public static void EnsureCreated()
        {
            foreach (var dir in new[] { DataRoot, Bin, Tools, Bases, Keys, Repo, Configs, Logs })
                Directory.CreateDirectory(dir);
        }
    }
}
