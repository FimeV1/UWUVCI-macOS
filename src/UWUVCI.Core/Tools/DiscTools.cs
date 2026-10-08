using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using nfs2iso2nfs;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    /// <summary>In-process replacements for nfs2iso2nfs.exe and the NKit ConvertToISO / ConvertToNKit executables.</summary>
    public static class DiscTools
    {
        public sealed class NfsOptions
        {
            public bool Homebrew, Passthrough, Horizontal, Wiimote, InstantCC, NoCC, LRPatch;
        }

        /// <summary>
        /// Turns content/game.iso of the working base into the hif_*.nfs files the Wii VC wrapper boots, and patches fw.img.
        /// Equivalent to running "nfs2iso2nfs.exe -enc ... -iso game.iso" inside the content folder.
        /// </summary>
        public static void IsoToNfs(string contentDirectory, NfsOptions options)
        {
            string codeDirectory = Path.Combine(Path.GetDirectoryName(contentDirectory.TrimEnd(Path.DirectorySeparatorChar)), "code");
            string key = Path.Combine(codeDirectory, "htk.bin");
            string fwImage = Path.Combine(codeDirectory, "fw.img");
            if (!File.Exists(key))
                throw new FileNotFoundException("The base is missing code/htk.bin, it can't be used for Wii/GameCube injects.");
            if (!File.Exists(fwImage))
                throw new FileNotFoundException("The base is missing code/fw.img, it can't be used for Wii/GameCube injects.");

            // The library reads and writes its intermediate files relative to the current directory.
            string previous = Directory.GetCurrentDirectory();
            TextWriter console = Console.Out;
            Directory.SetCurrentDirectory(contentDirectory);
            try
            {
                var config = new PackConfiguration
                {
                    IsEncrypted = true,
                    KeyFilePath = key,
                    FwImageFilePath = fwImage,
                    IsoFilePath = Path.Combine(contentDirectory, "game.iso"),
                    NfsDirectory = contentDirectory,
                    OutputDirectory = contentDirectory,
                    HomebrewPatches = options.Homebrew,
                    PassthroughMode = options.Passthrough,
                    HorizontalWiimote = options.Horizontal,
                    VerticalWiimote = options.Wiimote,
                    InstantCC = options.InstantCC,
                    NoClassicController = options.NoCC,
                    MapShoulderToTrigger = options.LRPatch,
                };
                var pack = new Pack(config, new LogAdapter());

                // The library never copies the patch switches from its configuration onto the patcher it runs,
                // so without this every inject would get an unpatched fw.img.
                object patch = typeof(Pack).GetField("_patch", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pack);
                if (patch == null)
                    throw new InvalidOperationException("nfs2iso2nfs.dll has an unexpected layout (no patcher found).");
                void Set(string property, bool value) => patch.GetType().GetProperty(property).SetValue(patch, value);
                Set("KeepLegit", false);
                Set("MapShoulderToTrigger", options.LRPatch);
                Set("HorizWiimote", options.Horizontal);
                Set("VertWiimote", options.Wiimote);
                Set("Homebrew", options.Homebrew);
                Set("PassThrough", options.Passthrough);
                Set("InstantCC", options.InstantCC);
                Set("NoCC", options.NoCC);

                // It also builds its output folder as Combine(cwd, "\"), which is the root of the disk anywhere but Windows.
                object nfs = typeof(Pack).GetField("_nfs", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pack);
                PropertyInfo outputDirectory = nfs?.GetType().GetProperty("NfsOutputDirectory");
                if (outputDirectory == null)
                    throw new InvalidOperationException("nfs2iso2nfs.dll has an unexpected layout (no output folder found).");
                outputDirectory.SetValue(nfs, contentDirectory);

                Console.SetOut(TextWriter.Null);
                pack.ConvertAsync().GetAwaiter().GetResult();
            }
            finally
            {
                Console.SetOut(console);
                Directory.SetCurrentDirectory(previous);
            }

            if (!File.Exists(Path.Combine(contentDirectory, "hif_000000.nfs")))
                throw new Exception("WII: An error occured while converting the ISO to NFS");
        }

        private sealed class LogAdapter : ILogger<Pack>
        {
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => Logger.Log("nfs2iso2nfs: " + formatter(state, exception));
        }

        // ---- NKit ---------------------------------------------------------------------------------------------------

        private static Assembly nkit;

        private static Assembly LoadNKit()
        {
            if (nkit != null)
                return nkit;

            string dll = Path.Combine(AppPaths.Tools, "NKit.dll");
            string configPath = dll + ".config";
            if (!File.Exists(dll) || !File.Exists(configPath))
                throw new FileNotFoundException("NKit.dll is missing from the tools folder.");

            // NKit writes next to the running program by default ("%exe"), which is inside the read-only app bundle.
            // Point its working path at the tools folder instead; that is also where the Windows app picks the output up.
            string config = File.ReadAllText(configPath);
            string wanted = $"<add key=\"Path\" value=\"{AppPaths.Tools.Replace("&", "&amp;")}{Path.DirectorySeparatorChar}\"/>";
            string patched = System.Text.RegularExpressions.Regex.Replace(config, "<add key=\"Path\" value=\"[^\"]*\"\\s*/>", wanted.Replace("$", "$$"));
            if (patched != config)
                File.WriteAllText(configPath, patched);

            MakeAnyCpu(dll);
            MakeAnyCpu(Path.Combine(AppPaths.Tools, "SharpCompress.dll"));

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string candidate = Path.Combine(AppPaths.Tools, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            return nkit = Assembly.LoadFrom(dll);
        }

        private static void OnNKitMessage(object sender, EventArgs e)
        {
            string message = e.GetType().GetProperty("Message")?.GetValue(e) as string;
            if (!string.IsNullOrWhiteSpace(message))
                Logger.Log("NKit: " + message);
        }

        /// <summary>
        /// NKit is pure IL but flagged "32-bit only", which no 64-bit runtime will load. Clears that flag in place.
        /// </summary>
        private static void MakeAnyCpu(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                return;
            byte[] pe = File.ReadAllBytes(assemblyPath);
            try
            {
                int peHeader = BitConverter.ToInt32(pe, 0x3C);
                int optional = peHeader + 24;
                bool pe32Plus = BitConverter.ToUInt16(pe, optional) == 0x20B;
                int cliDirectory = optional + (pe32Plus ? 112 : 96) + 14 * 8;
                uint cliRva = BitConverter.ToUInt32(pe, cliDirectory);
                if (cliRva == 0)
                    return;

                int sections = BitConverter.ToUInt16(pe, peHeader + 6);
                int sectionTable = optional + BitConverter.ToUInt16(pe, peHeader + 20);
                for (int i = 0; i < sections; i++)
                {
                    int s = sectionTable + i * 40;
                    uint virtualSize = BitConverter.ToUInt32(pe, s + 8), virtualAddress = BitConverter.ToUInt32(pe, s + 12), rawPointer = BitConverter.ToUInt32(pe, s + 20);
                    if (cliRva < virtualAddress || cliRva >= virtualAddress + Math.Max(virtualSize, 1))
                        continue;

                    int flagsOffset = (int)(cliRva - virtualAddress + rawPointer) + 16;
                    uint flags = BitConverter.ToUInt32(pe, flagsOffset);
                    const uint ILOnly = 1, Required32Bit = 2, Preferred32Bit = 0x20000;
                    if ((flags & ILOnly) != 0 && (flags & (Required32Bit | Preferred32Bit)) != 0)
                    {
                        BitConverter.GetBytes(flags & ~(Required32Bit | Preferred32Bit)).CopyTo(pe, flagsOffset);
                        File.WriteAllBytes(assemblyPath, pe);
                        Logger.Log($"Cleared the 32-bit flag of {Path.GetFileName(assemblyPath)}");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not inspect {assemblyPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a GameCube/Wii image with NKit and returns the path of the produced file (in the tools folder).
        /// </summary>
        public static string NKitConvert(string sourcePath, bool toIso)
        {
            Assembly assembly = LoadNKit();
            foreach (string stale in Directory.GetFiles(AppPaths.Tools, "out*.iso"))
                File.Delete(stale);

            Type sourceFiles = assembly.GetType("Nanook.NKit.SourceFiles", true);
            Type converterType = assembly.GetType("Nanook.NKit.Converter", true);
            var scanned = (Array)sourceFiles.GetMethod("Scan", new[] { typeof(string[]), typeof(bool) })
                .Invoke(null, new object[] { new[] { sourcePath }, false });
            if (scanned.Length == 0)
                throw new Exception("nkit");

            object converter = Activator.CreateInstance(converterType, scanned.GetValue(0), true);
            EventInfo logEvent = converterType.GetEvent("LogMessage");
            if (logEvent != null)
                logEvent.AddEventHandler(converter, Delegate.CreateDelegate(logEvent.EventHandlerType,
                    typeof(DiscTools).GetMethod(nameof(OnNKitMessage), BindingFlags.Static | BindingFlags.NonPublic)));
            try
            {
                converterType.GetMethod(toIso ? "ConvertToIso" : "ConvertToNkit", Type.EmptyTypes).Invoke(converter, null);
            }
            catch (TargetInvocationException ex)
            {
                Logger.Log($"NKit failed: {ex.InnerException?.Message ?? ex.Message}");
                throw new Exception("nkit");
            }
            finally
            {
                (converter as IDisposable)?.Dispose();
            }

            string result = Directory.GetFiles(AppPaths.Tools, "out*.iso").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (result == null)
                throw new Exception("nkit");
            return result;
        }
    }
}
