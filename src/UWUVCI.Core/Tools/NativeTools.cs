using System;
using System.Collections.Generic;
using System.IO;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    /// <summary>
    /// Wiimms ISO Tools (wit) and SZS Tools (wstrt): the two external programs with no managed replacement.
    /// </summary>
    public static class NativeTools
    {
        private static readonly Dictionary<string, string> resolved = new Dictionary<string, string>();

        public static string Wit => Resolve("wit");
        public static string Wstrt => Resolve("wstrt");

        public static ProcessResult RunWit(params string[] args) => ProcessRunner.Run(Wit, args);
        public static ProcessResult RunWstrt(params string[] args) => ProcessRunner.Run(Wstrt, args);

        private static string Resolve(string name)
        {
            lock (resolved)
            {
                if (resolved.TryGetValue(name, out string cached) && File.Exists(cached))
                    return cached;

                string path = Locate(name);
                resolved[name] = path;
                return path;
            }
        }

        private static string Locate(string name)
        {
            string over = Environment.GetEnvironmentVariable("UWUVCI_" + name.ToUpperInvariant());
            if (!string.IsNullOrEmpty(over) && File.Exists(over))
                return over;

            if (AppPaths.IsWindows)
            {
                string exe = Path.Combine(AppPaths.Tools, name + ".exe");
                if (File.Exists(exe))
                    return exe;
                throw new FileNotFoundException($"{name}.exe not found in {AppPaths.Tools}");
            }

            string file = name + (AppPaths.IsMac ? "-mac" : "-linux");
            string installed = Path.Combine(AppPaths.Tools, file);
            string bundled = Path.Combine(AppPaths.Bundled, "native", file);

            // Run from a private copy: files inside a downloaded .app carry a quarantine flag and a signature that
            // macOS kills the tool for; the copy can be cleaned and ad-hoc signed.
            if (File.Exists(bundled) && (!File.Exists(installed) || new FileInfo(installed).Length != new FileInfo(bundled).Length))
            {
                Directory.CreateDirectory(AppPaths.Tools);
                File.Copy(bundled, installed, true);
                Prepare(installed);
            }
            else if (File.Exists(installed) && !File.Exists(installed + ".ok"))
                Prepare(installed);

            if (!File.Exists(installed))
                throw new FileNotFoundException($"{file} is missing. It should be in {AppPaths.Tools}");
            return installed;
        }

        private static void Prepare(string path)
        {
            try
            {
                ProcessRunner.Run("/bin/chmod", new[] { "+x", path }, log: false);
                if (AppPaths.IsMac)
                {
                    ProcessRunner.Run("/usr/bin/xattr", new[] { "-d", "com.apple.quarantine", path }, log: false);
                    ProcessRunner.Run("/usr/bin/codesign", new[] { "--force", "-s", "-", path }, log: false);
                }
                File.WriteAllText(path + ".ok", "");
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not prepare {path}: {ex.Message}");
            }
        }
    }
}
