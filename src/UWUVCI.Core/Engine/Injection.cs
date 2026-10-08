using GameBaseClassLibrary;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using UWUVCI.Core.Tools;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Classes;
using UWUVCI_AIO_WPF.Helpers;
using UWUVCI_AIO_WPF.Models;

namespace UWUVCI.Core.Engine
{
    public static class StringExtensions
    {
        public static string ToHex(this string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in input)
                sb.AppendFormat("{0:X2}", (int)c);
            return sb.ToString().Trim();
        }
    }

    /// <summary>
    /// The injector. A port of Classes/Injection.cs of the Windows app in which every external Windows tool is replaced
    /// by managed code (see the Tools folder) or by the native macOS builds of wit / wstrt.
    /// Only one injection can run at a time: the working folder is shared.
    /// </summary>
    public static class Injection
    {
        private static string tempPath => AppPaths.Temp;
        private static string baseRomPath => Path.Combine(tempPath, "baserom");
        private static string imgPath => Path.Combine(tempPath, "img");
        private static string toolsPath => AppPaths.Tools;

        static InjectContext mvvm;

        static List<int> fiind(this byte[] buffer, byte[] pattern, int startIndex)
        {
            List<int> positions = new List<int>();
            int i = Array.IndexOf(buffer, pattern[0], startIndex);
            while (i >= 0 && i <= buffer.Length - pattern.Length)
            {
                byte[] segment = new byte[pattern.Length];
                Buffer.BlockCopy(buffer, i, segment, 0, pattern.Length);
                if (segment.SequenceEqual(pattern))
                    positions.Add(i);
                i = Array.IndexOf(buffer, pattern[0], i + 1);
            }
            return positions;
        }

        static void PokePatch(string rom)
        {
            byte[] search = { 0xD0, 0x88, 0x8D, 0x83, 0x42 };
            byte[] test = new byte[new FileInfo(rom).Length];
            using var fs = new FileStream(rom, FileMode.Open, FileAccess.ReadWrite);
            try
            {
                fs.Read(test, 0, test.Length - 1);

                var l = fiind(test, search, 0);
                for (int n = 0; n < 2; n++)
                {
                    byte[] check = new byte[4];
                    fs.Seek(l[n] + 5, SeekOrigin.Begin);
                    fs.Read(check, 0, 4);
                    fs.Seek(l[n] + 5, SeekOrigin.Begin);
                    if (check[3] != 0x24)
                        fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4);
                    else
                        fs.Write(new byte[] { 0x00, 0x00, 0x00 }, 0, 3);
                }
            }
            catch (Exception)
            {
                // ROMs without the pattern are left alone.
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
                dblSByte = bytes / 1024.0;

            return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        /// <summary>
        /// Builds the injected title in the working folder. Throws <see cref="InjectionException"/> with a readable
        /// message on failure; follow up with <see cref="Packing"/> or <see cref="Loadiine"/> to export it.
        /// </summary>
        public static void Inject(GameConfig Configuration, string RomPath, InjectContext mvm, bool force)
        {
            mvvm = mvm;
            mvm.Warnings.Clear();
            Clean();
            Directory.CreateDirectory(tempPath);

            long freeSpaceInBytes = 0;
            if (!mvm.saveworkaround)
            {
                try
                {
                    freeSpaceInBytes = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(tempPath))).AvailableFreeSpace;
                    if (AppPaths.IsMac)
                        freeSpaceInBytes = FreeSpaceOf(tempPath, freeSpaceInBytes);
                }
                catch (Exception)
                {
                    mvm.saveworkaround = true;
                }
            }

            mvm.Progress = 5;
            mvm.msg = "Copying Base...";
            try
            {
                if (!mvm.saveworkaround && (Configuration.Console == GameConsoles.WII || Configuration.Console == GameConsoles.GCN))
                {
                    long neededspace = mvm.GC ? 10000000000 : 15000000000;

                    if (freeSpaceInBytes < neededspace)
                        throw new Exception("12G");
                }

                if (Configuration.BaseRom == null || Configuration.BaseRom.Name == null)
                    throw new Exception("BASE");

                if (Configuration.BaseRom.Name != "Custom")
                    CopyBase($"{Configuration.BaseRom.Name.Replace(":", "")} [{Configuration.BaseRom.Region}]", null);
                else
                    CopyBase("Custom", Configuration.CBasePath);

                if (!Directory.Exists(Path.Combine(baseRomPath, "code")) || !Directory.Exists(Path.Combine(baseRomPath, "content")) || !Directory.Exists(Path.Combine(baseRomPath, "meta")))
                    throw new Exception("MISSINGF");

                mvm.Progress = 10;
                mvm.msg = "Injecting ROM...";

                RunSpecificInjection(Configuration, (mvm.GC ? GameConsoles.GCN : Configuration.Console), RomPath, force, mvm);

                mvm.msg = "Editing XML...";
                EditXML(Configuration.GameName, mvm.Index);
                mvm.Progress = 90;
                mvm.msg = "Changing Images...";
                Images(Configuration);
                if (File.Exists(mvm.BootSound))
                {
                    mvm.Progress = 95;
                    mvm.msg = "Adding BootSound...";
                    BootSoundTool.Create(mvm.BootSound, Path.Combine(baseRomPath, "meta", "bootSound.btsnd"), tempPath);
                }

                mvm.Progress = 100;
            }
            catch (Exception e)
            {
                Logger.Log(e.ToString());
                mvm.Progress = 100;

                var errorMessage = "Injection Failed due to unknown circumstances.";

                if (e.Message == "MISSINGF")
                    errorMessage = "Injection Failed because there are base files missing. \nPlease redownload the base, or redump if you used a custom base!";
                else if (e.Message.Contains("Images"))
                    errorMessage = "Injection Failed because one of the images could not be read or converted.\nTry a different image file (PNG, JPG, BMP or TGA).";
                else if (e.Message.Contains("retro"))
                    errorMessage = "The ROM you want to Inject is to big for selected Base!\nPlease try again with different Base";
                else if (e.Message.Contains("BASE"))
                    errorMessage = "If you import a config you NEED to reselect a base";
                else if (e.Message.Contains("WII"))
                    errorMessage = $"{e.Message.Replace("WII", "Wii")}\nPlease make sure that your ROM isn't flawed and that you have atleast 12 GB of free Storage left.";
                else if (e.Message.Contains("12G"))
                    errorMessage = $"Please make sure to have atleast {FormatBytes(15000000000)} of storage left on your startup disk.";
                else if (e.Message.Contains("nkit"))
                    errorMessage = "There is an issue with your NKIT / GCZ.\nPlease try the original ISO, or redump your game and try again with that dump.";
                else if (e.Message.Contains("meta.xml"))
                    errorMessage = "Looks to be your meta.xml file is missing from your base. If you downloaded your base, redownload it, if it's a custom base then the folder selected might be wrong or the layout is messed up.";
                else if (e.Message.Contains("pre.iso"))
                    errorMessage = "Looks to be that there is something about your game that UWUVCI doesn't like, you are most likely injecting with a wbfs or nkit.iso file, this file has data trimmed.";

                Clean();
                throw new InjectionException(errorMessage + "\n\nError Message:\n" + e.Message, e);
            }
            finally
            {
                mvm.Index = -1;
                mvm.LR = false;
                mvm.msg = "";
            }
        }

        private static long FreeSpaceOf(string path, long fallback)
        {
            // DriveInfo on "/" reports the sealed system volume on macOS; ask about the volume the folder is really on.
            try
            {
                var best = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && Path.GetFullPath(path).StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
                    .OrderByDescending(d => d.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                return best?.AvailableFreeSpace ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void RunSpecificInjection(GameConfig cfg, GameConsoles console, string RomPath, bool force, InjectContext mvm)
        {
            switch (console)
            {
                case GameConsoles.NDS:
                    NDS(RomPath);
                    break;
                case GameConsoles.N64:
                    N64(RomPath, cfg.N64Stuff);
                    break;
                case GameConsoles.GBA:
                    GBA(RomPath, cfg.GBAStuff);
                    break;
                case GameConsoles.NES:
                    NESSNES(RomPath);
                    break;
                case GameConsoles.SNES:
                    NESSNES(RemoveHeader(RomPath));
                    break;
                case GameConsoles.TG16:
                    TG16(RomPath);
                    break;
                case GameConsoles.MSX:
                    MSX(RomPath);
                    break;
                case GameConsoles.WII:
                    if (RomPath.ToLower().EndsWith(".dol"))
                        WiiHomebrew(RomPath, mvm);
                    else if (RomPath.ToLower().EndsWith(".wad"))
                        WiiForwarder(RomPath, mvm);
                    else
                        WII(RomPath, mvm);
                    break;
                case GameConsoles.GCN:
                    GC(RomPath, mvm, force);
                    break;
            }
        }

        private static string ByteArrayToString(byte[] arr) => new ASCIIEncoding().GetString(arr);

        private static string ReadDiscId(string isoPath)
        {
            byte[] chars = new byte[4];
            using (FileStream fstrm = new FileStream(isoPath, FileMode.Open, FileAccess.Read))
                fstrm.Read(chars, 0, 4);
            return ByteArrayToString(chars);
        }

        private static void SetReservedFlag2(string value)
        {
            string metaXml = Path.Combine(baseRomPath, "meta", "meta.xml");
            XmlDocument doc = new XmlDocument();
            doc.Load(metaXml);
            doc.SelectSingleNode("menu/reserved_flag2").InnerText = value;
            doc.Save(metaXml);
        }

        private static void Wit(string failure, params string[] args)
        {
            ProcessResult result = NativeTools.RunWit(args);
            if (!result.Ok)
                throw new Exception($"WII: {failure}\n{LastLines(result.Error.Length > 0 ? result.Error : result.Output)}");
        }

        private static string LastLines(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToArray();
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 4)));
        }

        private static void ExtractNintendontBase()
        {
            string tempBase = Path.Combine(tempPath, "TempBase");
            if (Directory.Exists(tempBase))
                Directory.Delete(tempBase, true);
            if (Directory.Exists(Path.Combine(tempPath, "BASE")))
                Directory.Delete(Path.Combine(tempPath, "BASE"), true);

            Directory.CreateDirectory(tempBase);
            ZipFile.ExtractToDirectory(Path.Combine(toolsPath, "BASE.zip"), tempPath);
            DirectoryCopy(Path.Combine(tempPath, "BASE"), tempBase, true);
        }

        private static void WiiForwarder(string romPath, InjectContext mvm)
        {
            mvvm.msg = "Extracting Forwarder Base...";
            ExtractNintendontBase();
            mvvm.Progress = 20;
            mvvm.msg = "Setting up Forwarder...";
            byte[] test = new byte[4];
            using (FileStream fs = new FileStream(romPath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(0xC20, SeekOrigin.Begin);
                fs.Read(test, 0, 4);
            }

            string[] id = { ByteArrayToString(test) };
            File.WriteAllLines(Path.Combine(tempPath, "TempBase", "files", "title.txt"), id);
            mvm.Progress = 30;
            mvm.msg = "Copying Forwarder...";
            File.Copy(Path.Combine(toolsPath, "forwarder.dol"), Path.Combine(tempPath, "TempBase", "sys", "main.dol"), true);
            mvm.Progress = 40;
            mvvm.msg = "Creating Injectable file...";
            SharedWitAndNFS2ISO2NFS(mvm, "WiiForwarder");
        }

        private static void WiiHomebrew(string romPath, InjectContext mvm)
        {
            mvvm.msg = "Extracting Homebrew Base...";
            ExtractNintendontBase();
            mvvm.Progress = 20;
            mvvm.msg = "Injecting DOL...";

            File.Copy(romPath, Path.Combine(tempPath, "TempBase", "sys", "main.dol"), true);
            mvm.Progress = 30;
            mvvm.msg = "Creating Injectable file...";
            SharedWitAndNFS2ISO2NFS(mvm, "WiiHomebrew");
        }

        private static void SharedWitAndNFS2ISO2NFS(InjectContext mvm, string functionName)
        {
            string gameIso = Path.Combine(tempPath, "game.iso");
            Wit("An error occured while Creating the ISO", "copy", Path.Combine(tempPath, "TempBase"), "--DEST", gameIso, "-ovv", "--links", "--iso");
            if (!File.Exists(gameIso))
                throw new Exception("WII: An error occured while Creating the ISO");

            mvvm.Progress = 50;

            if (functionName == "GCN")
            {
                mvm.msg = "Trying to save rom code...";
                SetReservedFlag2(ReadDiscId(Path.Combine(tempPath, "TempBase", "files", "game.iso")).ToHex());
                mvvm.Progress = 55;
            }

            Directory.Delete(Path.Combine(tempPath, "TempBase"), true);
            ReplaceTicketAndTmd(mvm, gameIso);

            var options = new DiscTools.NfsOptions { Homebrew = true };
            if (functionName != "GCN")
            {
                options.Passthrough = mvm.passtrough;
                ApplyGamePadMode(options, mvm);
            }
            else
                options.Passthrough = true;

            MoveIsoAndBuildNfs(mvm, gameIso, options);
        }

        private static void ApplyGamePadMode(DiscTools.NfsOptions options, InjectContext mvm)
        {
            if (mvm.Index == 2) options.Horizontal = true;
            if (mvm.Index == 3) options.Wiimote = true;
            if (mvm.Index == 4) options.InstantCC = true;
            if (mvm.Index == 5) options.NoCC = true;
            if (mvm.LR) options.LRPatch = true;
        }

        private static void ReplaceTicketAndTmd(InjectContext mvm, string gameIso)
        {
            mvm.msg = "Replacing TIK and TMD...";
            string tikTmd = Path.Combine(tempPath, "TIKTMD");
            if (Directory.Exists(tikTmd))
                Directory.Delete(tikTmd, true);
            Wit("An error occured while reading the ticket of the ISO", "extract", gameIso, "--psel", "data", "--files", "+tmd.bin", "--files", "+ticket.bin", "--DEST", tikTmd, "-vv1");

            foreach (string sFile in Directory.GetFiles(Path.Combine(baseRomPath, "code"), "rvlt.*"))
                File.Delete(sFile);

            File.Copy(Path.Combine(tikTmd, "tmd.bin"), Path.Combine(baseRomPath, "code", "rvlt.tmd"));
            File.Copy(Path.Combine(tikTmd, "ticket.bin"), Path.Combine(baseRomPath, "code", "rvlt.tik"));
            Directory.Delete(tikTmd, true);
            mvm.Progress = 60;
        }

        private static void MoveIsoAndBuildNfs(InjectContext mvm, string gameIso, DiscTools.NfsOptions options)
        {
            mvm.msg = "Injecting ROM...";
            string content = Path.Combine(baseRomPath, "content");
            foreach (string sFile in Directory.GetFiles(content, "*.nfs"))
                File.Delete(sFile);

            File.Move(gameIso, Path.Combine(content, "game.iso"));
            try
            {
                DiscTools.IsoToNfs(content, options);
            }
            finally
            {
                File.Delete(Path.Combine(content, "game.iso"));
            }
            mvm.Progress = 80;
        }

        private static List<string> ConvertGctFiles(InjectContext mvm)
        {
            var filePaths = mvm.GctPath.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var convertedGctFiles = new List<string>();

            foreach (var path in filePaths)
            {
                string convertedPath = path;

                if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var (codes, gameId) = GctCode.ParseOcarinaOrDolphinTxtFile(path);
                        convertedPath = Path.Combine(tempPath, Path.GetFileNameWithoutExtension(path) + ".gct");
                        GctCode.WriteGctFile(convertedPath, codes, gameId);
                        Logger.Log($"Converted {path} → {convertedPath} (Game ID: {gameId ?? "None"})");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ERROR: Failed to convert {path} - {ex.Message}");
                        mvm.Warnings.Add($"The cheat file {Path.GetFileName(path)} could not be read and was skipped.");
                        continue;
                    }
                }

                convertedGctFiles.Add(convertedPath);
            }
            return convertedGctFiles;
        }

        private static void GctPatch(InjectContext mvm, string extractionFolder)
        {
            if (string.IsNullOrEmpty(mvm.GctPath))
                return;

            mvm.msg = "Patching main.dol with gct file";
            mvm.Progress = 27;

            var mainDolPath = Directory.GetFiles(extractionFolder, "main.dol", SearchOption.AllDirectories).FirstOrDefault();
            List<string> gctFiles = ConvertGctFiles(mvm);
            if (gctFiles.Count == 0 || mainDolPath == null)
            {
                Logger.Log("ERROR: No valid GCT files available for patching.");
                return;
            }

            var args = new List<string> { "patch", mainDolPath };
            foreach (var gctFile in gctFiles)
            {
                args.Add("--add-section");
                args.Add(gctFile);
            }
            ProcessResult result;
            try
            {
                result = NativeTools.RunWstrt(args.ToArray());
            }
            catch (System.ComponentModel.Win32Exception ex) when (AppPaths.IsMac)
            {
                // wstrt only exists as an Intel binary; on Apple Silicon it can't start without Rosetta.
                throw new Exception("WII: The cheat code patcher needs Rosetta on Apple Silicon Macs.\nInstall it by running this in Terminal, then inject again:\nsoftwareupdate --install-rosetta\n(" + ex.Message + ")");
            }
            if (!result.Ok)
                throw new Exception("WII: The cheat codes could not be patched into the game.\n" + LastLines(result.Error.Length > 0 ? result.Error : result.Output));
        }

        private static void WII(string romPath, InjectContext mvm)
        {
            var dolPatch = mvm.RemoveDeflicker || mvm.RemoveDithering || mvm.HalfVFilter;
            string preIso = Path.Combine(tempPath, "pre.iso");
            string extension = new FileInfo(romPath).Extension.ToLower();
            bool nkit = mvm.NKITFLAG || romPath.ToLower().Contains("nkit");

            if (extension.Contains("iso") && !nkit)
            {
                mvm.msg = "Copying ROM...";
                File.Copy(romPath, preIso);
                mvm.Progress = 15;
            }
            else if (nkit)
            {
                mvm.msg = "Converting NKIT to ISO";
                File.Move(DiscTools.NKitConvert(romPath, toIso: true), preIso);
                mvm.Progress = 15;
            }
            else
            {
                mvm.msg = "Converting WBFS to ISO...";
                ProcessResult result = NativeTools.RunWit("copy", "--source", romPath, "--dest", preIso, "-I");
                if (!result.Ok || !File.Exists(preIso))
                    throw new Exception("pre.iso");
                mvm.Progress = 15;
            }

            mvm.msg = "Trying to change the Manual...";
            SetReservedFlag2(ReadDiscId(preIso).ToHex());
            mvm.Progress = 25;

            if (mvm.regionfrii)
            {
                using FileStream fs = new FileStream(preIso, FileMode.Open);
                fs.Seek(0x4E003, SeekOrigin.Begin);
                if (mvm.regionfriius)
                {
                    fs.Write(new byte[] { 0x01 }, 0, 1);
                    fs.Seek(0x4E010, SeekOrigin.Begin);
                    fs.Write(new byte[] { 0x80, 0x06, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, 0, 16);
                }
                else if (mvm.regionfriijp)
                {
                    fs.Write(new byte[] { 0x00 }, 0, 1);
                    fs.Seek(0x4E010, SeekOrigin.Begin);
                    fs.Write(new byte[] { 0x00, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, 0, 16);
                }
                else
                {
                    fs.Write(new byte[] { 0x02 }, 0, 1);
                    fs.Seek(0x4E010, SeekOrigin.Begin);
                    fs.Write(new byte[] { 0x80, 0x80, 0x80, 0x00, 0x03, 0x03, 0x04, 0x03, 0x00, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, 0, 16);
                }
            }

            string tempFolder = Path.Combine(tempPath, "TEMP");
            mvm.msg = mvm.donttrim ? "Prepping ROM..." : "Trimming ROM...";
            Wit("An error occured while extracting the ROM", "extract", preIso, "--DEST", tempFolder, "--psel", mvm.donttrim ? "data" : "WHOLE", "-vv1");
            mvm.Progress = 30;

            // Everything from here on works on the extracted files; the original image is no longer needed.
            File.Delete(preIso);

            GctPatch(mvm, tempFolder);

            string mainDol = Directory.GetFiles(tempFolder, "main.dol", SearchOption.AllDirectories)
                .OrderBy(p => p.Replace('\\', '/').Contains("/DATA/") ? 0 : 1).FirstOrDefault();

            if (dolPatch && mainDol != null)
            {
                mvm.msg = "Patching main.dol file";
                mvm.Progress = 33;

                var output = Path.Combine(Path.GetDirectoryName(mainDol), "patched.dol");
                DeflickerDitheringRemover.ProcessFile(mainDol, output, mvm.RemoveDeflicker, mvm.RemoveDithering, mvm.HalfVFilter);

                File.Delete(mainDol);
                File.Move(output, mainDol);
            }

            if (mvm.Index == 4 && mainDol != null)
            {
                mvvm.msg = "Patching ROM (Force CC)...";
                if (!RomTools.PatchGetExtType(mainDol))
                    mvm.Warnings.Add("The Force Classic Controller patch found nothing to patch in this game.");
                mvm.Progress = 35;
            }

            if (mvm.Patch)
            {
                // wii-vmc.exe is a closed Windows-only program with no macOS equivalent.
                mvm.Warnings.Add("Video mode patching (NTSC ⇄ PAL) is not available on macOS and was skipped.");
                Logger.Log("Skipping video mode patch: not available on this platform.");
            }

            string gameIso = Path.Combine(tempPath, "game.iso");
            if (mvm.donttrim)
            {
                mvm.msg = "Creating ISO from patched ROM...";
                Wit("An error occured while Creating the ISO", "copy", tempFolder, "--DEST", gameIso, "-ovv", "--psel", "WHOLE", "--iso");
            }
            else
            {
                mvm.msg = "Creating ISO from trimmed ROM...";
                Wit("An error occured while Creating the ISO", "copy", tempFolder, "--DEST", gameIso, "-ovv", "--links", "--iso");
            }
            if (!File.Exists(gameIso))
                throw new Exception("WII: An error occured while Creating the ISO");

            Directory.Delete(tempFolder, true);

            mvm.Progress = 50;
            ReplaceTicketAndTmd(mvm, gameIso);

            var options = new DiscTools.NfsOptions();
            ApplyGamePadMode(options, mvm);
            MoveIsoAndBuildNfs(mvm, gameIso, options);
        }

        /// <summary>Puts a GameCube image into the Nintendont base as <paramref name="targetName"/>, trimmed unless told otherwise.</summary>
        private static void AddGameCubeDisc(string romPath, string targetName, InjectContext mvm)
        {
            string target = Path.Combine(tempPath, "TempBase", "files", targetName);
            string lower = romPath.ToLower();
            bool isNkit = lower.Contains("nkit");
            bool isGcz = lower.EndsWith(".gcz");

            if (mvm.donttrim)
            {
                if (isNkit || isGcz)
                    File.Move(DiscTools.NKitConvert(romPath, toIso: true), target);
                else
                    File.Copy(romPath, target);
                return;
            }

            if (isNkit && !isGcz)
            {
                File.Copy(romPath, target);
                return;
            }

            try
            {
                File.Move(DiscTools.NKitConvert(romPath, toIso: false), target);
            }
            catch (Exception ex) when (!isGcz)
            {
                // Trimming only saves space. A full image works just as well, so don't fail the inject over it.
                Logger.Log($"NKit could not trim {romPath}: {ex.Message}. Using the full image.");
                mvm.Warnings.Add($"{Path.GetFileName(romPath)} could not be trimmed, so the full disc image was used. The inject is larger but works the same.");
                if (File.Exists(target))
                    File.Delete(target);
                File.Copy(romPath, target);
            }
        }

        private static void GC(string romPath, InjectContext mvm, bool force)
        {
            mvvm.msg = "Extracting Nintendont Base...";
            ExtractNintendontBase();
            mvvm.Progress = 20;
            mvvm.msg = "Applying Nintendont" + (force ? " force 4:3..." : "...");
            File.Copy(Path.Combine(toolsPath, force ? "nintendont_force.dol" : "nintendont.dol"), Path.Combine(tempPath, "TempBase", "sys", "main.dol"), true);

            mvm.Progress = 40;
            mvvm.msg = "Injecting GameCube Game into NintendontBase...";
            AddGameCubeDisc(romPath, "game.iso", mvm);

            if (!string.IsNullOrEmpty(mvm.gc2rom) && File.Exists(mvm.gc2rom))
                AddGameCubeDisc(mvm.gc2rom, "disc2.iso", mvm);

            SharedWitAndNFS2ISO2NFS(mvm, "GCN");
        }

        public static void MSX(string injectRomPath)
        {
            mvvm.msg = "Reading Header from Base...";
            string pkg = Path.Combine(baseRomPath, "content", "msx", "msx.pkg");
            byte[] header = new byte[0x580B3];
            using (var fs = new FileStream(pkg, FileMode.Open, FileAccess.Read))
                fs.Read(header, 0, 0x580B3);
            mvvm.Progress = 20;

            mvvm.msg = "Reading ROM content...";
            byte[] rom = File.ReadAllBytes(injectRomPath);
            // The Windows tool never reads the final byte of the ROM; kept so injects come out identical.
            if (rom.Length > 0)
                rom[rom.Length - 1] = 0;
            mvvm.Progress = 50;

            mvvm.msg = "Injecting ROM into new PKG...";
            using (var fs = new FileStream(pkg, FileMode.Create, FileAccess.Write))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(rom, 0, rom.Length);
            }
            mvvm.Progress = 80;
        }

        public static void DeleteDirectory(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception) when (attempt < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }

        public static void Clean()
        {
            if (Directory.Exists(tempPath))
                DeleteDirectory(tempPath);
        }

        /// <summary>True when an injected title is sitting in the working folder, waiting to be exported.</summary>
        public static bool HasInjectedTitle => Directory.Exists(Path.Combine(baseRomPath, "code"));

        private static string UniqueOutputFolder(string name, InjectContext mvm)
        {
            Directory.CreateDirectory(JsonSettingsManager.Settings.OutPath);
            string outputPath = Path.Combine(JsonSettingsManager.Settings.OutPath, name);
            mvm.foldername = name;
            int i = 0;
            while (Directory.Exists(outputPath))
            {
                mvm.foldername = $"{name}_{i}";
                outputPath = Path.Combine(JsonSettingsManager.Settings.OutPath, mvm.foldername);
                i++;
            }
            return outputPath;
        }

        /// <summary>Exports the injected title as a Loadiine folder. Returns the output folder.</summary>
        public static string Loadiine(string gameName, string gameConsole, InjectContext mvm)
        {
            if (string.IsNullOrEmpty(gameName)) gameName = "NoName";
            gameName = gameName.Replace("|", " ");
            Regex reg = new Regex("[^a-zA-Z0-9 é -]");
            string outputPath = UniqueOutputFolder($"[LOADIINE][{gameConsole}] {reg.Replace(gameName, "")} [{mvm.prodcode}]", mvm);

            DirectoryCopy(baseRomPath, outputPath, true);
            Clean();
            return outputPath;
        }

        /// <summary>Exports the injected title as an installable WUP package. Returns the output folder.</summary>
        public static string Packing(string gameName, string gameConsole, InjectContext mvm)
        {
            mvm.Progress = 20;
            mvm.msg = "Creating Outputfolder...";
            Regex reg = new Regex("[^a-zA-Z0-9 -]");
            if (string.IsNullOrEmpty(gameName)) gameName = "NoName";

            string outputPath = UniqueOutputFolder($"[WUP][{gameConsole}] {reg.Replace(gameName, "").Replace("|", " ")}", mvm);
            mvm.Progress = 40;
            mvm.msg = "Packing...";
            try
            {
                NusPacker.Pack(baseRomPath, outputPath, JsonSettingsManager.Settings.Ckey);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.ToString());
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
                throw;
            }
            mvm.Progress = 90;
            mvm.msg = "Cleaning...";
            Clean();
            mvm.Progress = 100;
            mvm.msg = "";
            return outputPath;
        }

        // This function changes TitleID, ProductCode and GameName in app.xml (ID) and meta.xml (ID, ProductCode, Name)
        private static void EditXML(string gameNameOr, int index)
        {
            string gameName = string.Empty;
            if (!string.IsNullOrWhiteSpace(gameNameOr))
            {
                gameName = gameNameOr;
                if (gameName.Contains('|'))
                {
                    var split = gameName.Split('|');
                    gameName = split[0] + "," + split[1];
                }
            }

            string metaXml = Path.Combine(baseRomPath, "meta", "meta.xml");
            string appXml = Path.Combine(baseRomPath, "code", "app.xml");
            if (!File.Exists(metaXml))
                throw new FileNotFoundException("meta.xml is missing from the base.");

            Random random = new Random();
            string ID = $"{random.Next(0x3000, 0x10000):X4}{random.Next(0x3000, 0x10000):X4}";
            string ID2 = $"{random.Next(0x3000, 0x10000):X4}";
            mvvm.prodcode = ID2;

            string[] languages = { "ja", "en", "fr", "de", "it", "es", "zhs", "ko", "nl", "pt", "ru", "zht" };
            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(metaXml);
                if (!string.IsNullOrEmpty(gameName))
                    foreach (string language in languages)
                        doc.SelectSingleNode("menu/longname_" + language).InnerText = gameName.Replace(",", "\n");

                doc.SelectSingleNode("menu/product_code").InnerText = $"WUP-N-{ID2}";
                if (index > 0)
                    doc.SelectSingleNode("menu/drc_use").InnerText = "65537";

                doc.SelectSingleNode("menu/title_id").InnerText = $"00050002{ID}";
                doc.SelectSingleNode("menu/group_id").InnerText = $"0000{ID2}";
                if (!string.IsNullOrEmpty(gameName))
                    foreach (string language in languages)
                        doc.SelectSingleNode("menu/shortname_" + language).InnerText = gameName.Split(',')[0];

                doc.Save(metaXml);
            }
            catch (NullReferenceException)
            {
            }

            try
            {
                doc.Load(appXml);
                doc.SelectSingleNode("app/title_id").InnerText = $"00050002{ID}";
                doc.SelectSingleNode("app/group_id").InnerText = $"0000{ID2}";
                doc.Save(appXml);
            }
            catch (NullReferenceException)
            {
            }
        }

        //This function copies the custom or normal Base to the working directory
        private static void CopyBase(string baserom, string customPath)
        {
            if (Directory.Exists(baseRomPath)) // sanity check
                Directory.Delete(baseRomPath, true);

            if (baserom == "Custom")
                DirectoryCopy(customPath, baseRomPath, true);
            else
                DirectoryCopy(Path.Combine(JsonSettingsManager.Settings.BasePath, baserom), baseRomPath, true);
        }

        private static void TG16(string injectRomPath)
        {
            string pkg = Path.Combine(baseRomPath, "content", "pceemu", "pce.pkg");
            if (Directory.Exists(injectRomPath))
            {
                mvvm.msg = "Creating TurboCD Pkg...";
                RomTools.BuildTurboCdPcePkg(injectRomPath, pkg);
            }
            else
            {
                mvvm.msg = "Creating Turbo16 Pkg...";
                RomTools.BuildPcePkg(injectRomPath, pkg);
            }
            mvvm.Progress = 80;
        }

        private static void NESSNES(string injectRomPath)
        {
            string rpxFile = Directory.GetFiles(Path.Combine(baseRomPath, "code"), "*.rpx")[0]; //To get the RPX path where the NES/SNES rom needs to be Injected in
            mvvm.msg = "Decompressing RPX...";
            RpxTool.Decompress(rpxFile);
            mvvm.Progress = 20;
            if (mvvm.pixelperfect)
            {
                // ChangeAspectRatio.exe is a closed Windows-only program with no macOS equivalent.
                mvvm.Warnings.Add("Pixel Perfect mode is not available on macOS and was skipped.");
                Logger.Log("Skipping Pixel Perfect patch: not available on this platform.");
            }

            mvvm.msg = "Injecting ROM...";
            RomTools.RetroInject(rpxFile, injectRomPath, rpxFile);
            mvvm.Progress = 70;

            mvvm.msg = "Compressing RPX...";
            RpxTool.Compress(rpxFile);
            mvvm.Progress = 80;
        }

        private static void GBA(string injectRomPath, N64Conf config)
        {
            if (!new FileInfo(injectRomPath).Extension.ToLower().Contains("gba"))
            {
                mvvm.msg = "Injecting GB/GBC ROM into goomba...";

                // goomba.gba followed by the GB/GBC ROM, padded to 32 MB
                byte[] rom = new byte[33554432];
                byte[] goomba = File.ReadAllBytes(Path.Combine(toolsPath, "goomba.gba"));
                byte[] game = File.ReadAllBytes(injectRomPath);
                if (goomba.Length + game.Length > rom.Length)
                    throw new Exception("retro");
                Buffer.BlockCopy(goomba, 0, rom, 0, goomba.Length);
                Buffer.BlockCopy(game, 0, rom, goomba.Length, game.Length);
                mvvm.Progress = 20;

                mvvm.msg = "Padding goomba ROM...";
                injectRomPath = Path.Combine(tempPath, "goombaPadded.gba");
                File.WriteAllBytes(injectRomPath, rom);
                mvvm.Progress = 40;
            }

            if (mvvm.PokePatch)
            {
                mvvm.msg = "Applying PokePatch";
                string patched = Path.Combine(tempPath, "rom.gba");
                File.Copy(injectRomPath, patched, true);
                injectRomPath = patched;
                PokePatch(injectRomPath);
                mvvm.PokePatch = false;
                mvvm.Progress = 50;
            }

            mvvm.msg = "Injecting ROM...";
            GbaTool.Inject(Path.Combine(baseRomPath, "content"), injectRomPath, removeDarkFilter: !config.DarkFilter);
            mvvm.Progress = 80;
        }

        private static void NDS(string injectRomPath)
        {
            string zipLocation = Path.Combine(baseRomPath, "content", "0010", "rom.zip");

            mvvm.msg = "Getting BaseRom Name...";
            string romName = string.Empty;
            using (var zip = ZipFile.Open(zipLocation, ZipArchiveMode.Read))
            {
                var entry = zip.Entries.FirstOrDefault(file => file.Name.Contains("WUP"));
                if (entry != null)
                    romName = entry.Name;
            }
            mvvm.Progress = 15;

            if (string.IsNullOrEmpty(romName))
                throw new InvalidOperationException("ROM name not found in the zip archive.");

            mvvm.msg = "Removing BaseRom...";
            File.Delete(zipLocation);

            if (mvvm.DSLayout)
            {
                mvvm.msg = "Adding additional DS layout screens...";

                using (var zip = ZipFile.Open(Path.Combine(toolsPath, "DSLayoutScreens.zip"), ZipArchiveMode.Read))
                    zip.ExtractToDirectory(Path.Combine(tempPath, "DSLayoutScreens"));

                // "Phatnom" is how the folder is spelled inside the zip.
                DirectoryCopy(Path.Combine(tempPath, "DSLayoutScreens", (mvvm.STLayout ? "Phatnom Hourglass" : "All")), baseRomPath, true);
            }
            if (mvvm.Brightness != 80 || mvvm.PixelArtUpscaler != 0)
            {
                mvvm.msg = "Updating configuration_cafe.json...";
                UpdateConfigurationCafeJson();
            }

            using (var stream = new FileStream(zipLocation, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(injectRomPath, romName);
            mvvm.Progress = 80;
        }

        private static void UpdateConfigurationCafeJson()
        {
            var configurationCafe = Path.Combine(baseRomPath, "content", "0010", "configuration_cafe.json");
            var jsonObject = JObject.Parse(File.ReadAllText(configurationCafe));

            // Leave the base's default RenderScale unchanged.
            jsonObject["configuration"]["Display"]["Brightness"] = mvvm.Brightness;
            jsonObject["configuration"]["Display"]["PixelArtUpscaler"] = mvvm.PixelArtUpscaler;

            File.WriteAllText(configurationCafe, jsonObject.ToString());
        }

        private static void N64(string injectRomPath, N64Conf config)
        {
            string mainRomPath = Directory.GetFiles(Path.Combine(baseRomPath, "content", "rom"))[0];

            mvvm.msg = "Injecting ROM...";
            RomTools.ConvertN64(injectRomPath, mainRomPath);
            mvvm.Progress = 60;

            if (config.WideScreen || config.DarkFilter)
            {
                string frameLayoutPath = Path.Combine(baseRomPath, "content", "FrameLayout.arc");
                byte[] archive = File.ReadAllBytes(frameLayoutPath);
                N64FrameLayoutPatcher.Apply(archive, config.WideScreen, config.DarkFilter);
                File.WriteAllBytes(frameLayoutPath, archive);
                mvvm.Progress = 70;
            }

            mvvm.msg = "Copying INI...";
            string mainIni = Path.Combine(baseRomPath, "content", "config", $"{Path.GetFileName(mainRomPath)}.ini");
            File.Delete(mainIni);
            if (config.INIBin != null)
                File.WriteAllBytes(mainIni, config.INIBin);
            else if (config.INIPath != null)
                File.Copy(config.INIPath, mainIni);
            else
                File.Copy(Path.Combine(toolsPath, "blank.ini"), mainIni);
            mvvm.Progress = 80;
        }

        /// <summary>Returns the file to read an image from, writing embedded image data to disk first. Null when none is set.</summary>
        private static string ImageSource(PNGTGA image, string name)
        {
            if (image.ImgBin != null)
            {
                string extension = string.IsNullOrEmpty(image.extension) ? "png" : image.extension.TrimStart('.');
                string path = Path.Combine(imgPath, $"{name}.source.{extension}");
                File.WriteAllBytes(path, image.ImgBin);
                return path;
            }
            return string.IsNullOrEmpty(image.ImgPath) ? null : image.ImgPath;
        }

        private static void Images(GameConfig config)
        {
            try
            {
                if (Directory.Exists(imgPath)) // sanity check
                    Directory.Delete(imgPath, true);
                Directory.CreateDirectory(imgPath);

                string meta = Path.Combine(baseRomPath, "meta");

                string icon = ImageSource(config.TGAIco, "iconTex");
                if (icon == null && File.Exists(Path.Combine(toolsPath, "iconTex.tga")))
                    icon = Path.Combine(toolsPath, "iconTex.tga");

                string tv = ImageSource(config.TGATv, "bootTvTex");
                if (tv == null && File.Exists(Path.Combine(toolsPath, "bootTvTex.png")))
                    tv = Path.Combine(toolsPath, "bootTvTex.png");

                // Without a GamePad image of its own the GamePad shows the TV image.
                string drc = ImageSource(config.TGADrc, "bootDrcTex") ?? tv;
                string logo = ImageSource(config.TGALog, "bootLogoTex");

                if (icon != null) ImageTools.ConvertToTga(icon, Path.Combine(meta, "iconTex.tga"), 128, 128, 32);
                if (tv != null) ImageTools.ConvertToTga(tv, Path.Combine(meta, "bootTvTex.tga"), 1280, 720, 24);
                if (drc != null) ImageTools.ConvertToTga(drc, Path.Combine(meta, "bootDrcTex.tga"), 854, 480, 24);
                if (logo != null) ImageTools.ConvertToTga(logo, Path.Combine(meta, "bootLogoTex.tga"), 170, 42, 32);

                Directory.Delete(imgPath, true);
            }
            catch (Exception e)
            {
                Logger.Log(e.ToString());
                throw new Exception("Images: " + e.Message);
            }
        }

        private static string RemoveHeader(string filePath)
        {
            // logic taken from snesROMUtil
            using FileStream inStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] header = new byte[512];
            inStream.Read(header, 0, 512);
            string string1 = BitConverter.ToString(header, 8, 3);
            string string2 = Encoding.ASCII.GetString(header, 0, 11);
            string string3 = BitConverter.ToString(header, 30, 16);

            if (string1 != "AA-BB-04" && string2 != "GAME DOCTOR" && string3 != "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00")
                return filePath;

            string newFilePath = Path.Combine(tempPath, Path.GetFileName(filePath));
            using (FileStream outStream = new FileStream(newFilePath, FileMode.Create))
                inStream.CopyTo(outStream);

            return newFilePath;
        }

        public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                Logger.Log($"Source directory does not exist or could not be found: {sourceDirName}");
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDirName}");
            }

            if (!Directory.Exists(destDirName))
                Directory.CreateDirectory(destDirName);

            foreach (FileInfo file in dir.EnumerateFiles())
            {
                // Finder litter; it would end up inside the installed title.
                if (file.Name == ".DS_Store" || file.Name.StartsWith("._", StringComparison.Ordinal))
                    continue;
                file.CopyTo(Path.Combine(destDirName, file.Name), true);
            }

            if (copySubDirs)
                foreach (DirectoryInfo subdir in dir.EnumerateDirectories())
                    DirectoryCopy(subdir.FullName, Path.Combine(destDirName, subdir.Name), copySubDirs);
        }
    }
}
