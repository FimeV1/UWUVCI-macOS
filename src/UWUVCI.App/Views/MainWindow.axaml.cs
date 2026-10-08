using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameBaseClassLibrary;
using UWUVCI.App.ViewModels;
using UWUVCI.Core;
using UWUVCI.Core.Engine;
using UWUVCI.Core.Services;
using UWUVCI.Core.Tools;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.App.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel vm = new MainViewModel();
        private Process soundPlayer;

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = vm;

            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            DragDrop.SetAllowDrop(this, true);

            Opened += async (s, e) => await FirstRunAsync();
            Closing += (s, e) => StopSound();
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        /// <summary>Runs long work behind the progress overlay. Returns false (after telling the user) when it fails.</summary>
        private async Task<bool> RunBusy(string title, Func<IProgress<(int, string)>, Task> work, string failureTitle = null, bool indeterminate = false)
        {
            vm.BusyTitle = title;
            vm.BusyMessage = "";
            vm.BusyProgress = 0;
            vm.BusyIndeterminate = indeterminate;
            vm.IsBusy = true;
            var progress = new Progress<(int, string)>(p =>
            {
                vm.BusyProgress = p.Item1;
                if (!string.IsNullOrEmpty(p.Item2))
                    vm.BusyMessage = p.Item2;
            });
            try
            {
                await Task.Run(() => work(progress));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log(ex.ToString());
                vm.IsBusy = false;
                string message = ex is InjectionException ? ex.Message : Flatten(ex);
                await Dialogs.Error(this, failureTitle ?? "Something went wrong", message);
                return false;
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        private static string Flatten(Exception ex)
        {
            while (ex is AggregateException aggregate && aggregate.InnerException != null)
                ex = aggregate.InnerException;
            return ex.InnerException != null && ex.InnerException.Message != ex.Message ? $"{ex.Message}\n{ex.InnerException.Message}" : ex.Message;
        }

        private async Task FirstRunAsync()
        {
            if (ToolManager.MissingTools().Count == 0 && BaseManager.MissingLists().Count == 0)
                return;

            bool ok = await RunBusy("Downloading required files", async progress =>
            {
                await ToolManager.DownloadMissingAsync(new Progress<(int, string)>(p => progress.Report((p.Item1 * 8 / 10, p.Item2))));
                await BaseManager.DownloadListsAsync(false, new Progress<(int, string)>(p => progress.Report((80 + p.Item1 * 2 / 10, p.Item2))));
            }, "Could not download the required files");

            if (!ok)
                await Dialogs.Message(this, "Files are missing",
                    "UWUVCI needs to download a few files the first time it starts. Check your internet connection, then use Settings → Update tools and Update base lists.");
            vm.ReloadBases();
        }

        // ---- base ---------------------------------------------------------------------------------------------------

        private async void OnEnterCommonKey(object sender, RoutedEventArgs e)
        {
            string key = await Dialogs.Prompt(this, "Wii U Common Key", "Enter the Wii U Common Key (32 hexadecimal characters).", "32 characters");
            if (key == null)
                return;
            if (BaseManager.TrySetCommonKey(key))
                vm.RefreshBaseState();
            else
                await Dialogs.Error(this, "Wrong key", "That is not the Wii U Common Key. Check it for typos and try again.");
        }

        private async void OnEnterTitleKey(object sender, RoutedEventArgs e)
        {
            BaseItem item = vm.SelectedBase;
            if (item == null || item.IsCustom)
                return;
            string key = await Dialogs.Prompt(this, "Title Key", $"Enter the Title Key of {item.Name} (32 hexadecimal characters).", "32 characters");
            if (key == null)
                return;
            if (BaseManager.TrySetTitleKey(vm.Console, item.Base, key))
                vm.RefreshBaseState();
            else
                await Dialogs.Error(this, "Wrong key", $"That is not the Title Key of {item.Name}. Check it for typos and try again.");
        }

        private async void OnDownloadBase(object sender, RoutedEventArgs e)
        {
            BaseItem item = vm.SelectedBase;
            if (item == null || item.IsCustom)
                return;
            GameConsoles console = vm.Console;
            await RunBusy($"Downloading {item.Name}", progress => BaseManager.DownloadBaseAsync(console, item.Base, progress), "The base could not be downloaded");
            vm.RefreshBaseState();
        }

        private async void OnChooseCustomBase(object sender, RoutedEventArgs e)
        {
            string folder = await Dialogs.PickFolder(this, "Choose your base game folder");
            if (folder == null)
                return;

            switch (BaseManager.InspectCustomBase(folder))
            {
                case BaseManager.CustomBaseKind.Loadiine:
                    vm.CustomBasePath = folder;
                    break;
                case BaseManager.CustomBaseKind.Nus:
                    if (!BaseManager.IsCommonKeySet)
                    {
                        await Dialogs.Error(this, "Common Key needed", "This base is in NUS format (encrypted). Enter the Wii U Common Key first so it can be converted.");
                        return;
                    }
                    if (await Dialogs.Message(this, "NUS custom base", "You seem to have added a NUS format custom base.\nDo you want it to be converted to be used with the injector?", "Convert", "Cancel") != "Convert")
                        return;
                    GameConsoles console = vm.Console;
                    string converted = null;
                    if (await RunBusy("Converting base", progress => { converted = BaseManager.ExtractCustomBase(folder, console); return Task.CompletedTask; }, "The base could not be converted", indeterminate: true))
                        vm.CustomBasePath = converted;
                    break;
                default:
                    await Dialogs.Error(this, "Not a base", "That folder is not a Wii U base. It needs to contain the folders code, content and meta (or be a title in NUS format).");
                    break;
            }
        }

        // ---- files --------------------------------------------------------------------------------------------------

        private async void OnBrowseRom(object sender, RoutedEventArgs e)
        {
            string path = vm.TurboCd
                ? await Dialogs.PickFolder(this, "Choose the TurboGrafx-CD game folder")
                : await Dialogs.PickFile(this, "Choose the game to inject", vm.SelectedConsole.RomHint, vm.SelectedConsole.RomPatterns);
            if (path != null)
                await SetRomAsync(path);
        }

        /// <summary>Takes a new ROM: fills in what can be read from it and offers artwork from the community repository.</summary>
        private async Task SetRomAsync(string path)
        {
            vm.RomPath = path;
            GameConsoles console = vm.Console;
            RomDetails details;
            try
            {
                details = RomInfo.Read(path, console, vm.TurboCd);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not read ROM details of {path}: {ex.Message}");
                if (console == GameConsoles.WII || console == GameConsoles.GCN)
                    await Dialogs.Message(this, "Unknown ROM", $"This does not look like a {vm.SelectedConsole.Name} game.\nIt is not recommended continuing with it.");
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.GameName) && !string.IsNullOrWhiteSpace(details.InternalName))
                vm.GameName = details.InternalName.Trim();

            RepoArtwork artwork;
            try
            {
                artwork = await RomInfo.FindArtworkAsync(details, console);
            }
            catch (Exception ex)
            {
                Logger.Log("Artwork lookup failed: " + ex.Message);
                return;
            }
            // The user may have moved on while the lookup was running.
            if (artwork == null || vm.RomPath != path)
                return;

            var found = new List<string>();
            if (artwork.IconUrl != null) found.Add("images");
            if (artwork.IniUrl != null) found.Add("an INI file");
            if (artwork.BootSoundUrl != null) found.Add("a boot sound");
            if (await Dialogs.Message(this, "Artwork found", $"The UWUVCI image repository has {string.Join(", ", found)} for this game. Use them?", "Use", "No thanks") != "Use")
                return;

            await RunBusy("Downloading artwork", async progress =>
            {
                string id = artwork.RepoId?.Replace('/', '-') ?? "game";
                if (artwork.IconUrl != null)
                {
                    string icon = await RomInfo.DownloadAsync(artwork.IconUrl, $"{id}-iconTex{Path.GetExtension(artwork.IconUrl)}");
                    progress.Report((40, ""));
                    string tv = null;
                    try { tv = await RomInfo.DownloadAsync(artwork.TvUrl, $"{id}-bootTvTex{Path.GetExtension(artwork.TvUrl)}"); }
                    catch (Exception ex) { Logger.Log("No TV image in the repository: " + ex.Message); }
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        vm.IconPath = icon;
                        if (tv != null)
                            vm.TvPath = tv;
                    });
                }
                progress.Report((70, ""));
                if (artwork.IniUrl != null)
                {
                    string ini = await RomInfo.DownloadAsync(artwork.IniUrl, "game.ini");
                    await Dispatcher.UIThread.InvokeAsync(() => vm.IniPath = ini);
                }
                if (artwork.BootSoundUrl != null)
                {
                    string sound = await RomInfo.DownloadAsync(artwork.BootSoundUrl, "bootSound.btsnd");
                    await Dispatcher.UIThread.InvokeAsync(() => vm.BootSoundPath = sound);
                }
            }, "The artwork could not be downloaded");
        }

        private async void OnBrowseDisc2(object sender, RoutedEventArgs e)
        {
            string path = await Dialogs.PickFile(this, "Choose disc 2", "GameCube game", "*.iso", "*.gcm", "*.gcz");
            if (path != null)
                vm.Disc2Path = path;
        }

        private void SetImage(string kind, string path)
        {
            switch (kind)
            {
                case "icon": vm.IconPath = path; break;
                case "tv": vm.TvPath = path; break;
                case "drc": vm.DrcPath = path; break;
                case "logo": vm.LogoPath = path; break;
            }
        }

        private async void OnBrowseImage(object sender, RoutedEventArgs e)
        {
            string kind = (sender as Control)?.Tag as string;
            string path = await Dialogs.PickFile(this, "Choose an image", "Images", Dialogs.ImagePatterns);
            if (path != null)
                SetImage(kind, path);
        }

        private async void OnCreateImage(object sender, RoutedEventArgs e)
        {
            string kind = (sender as Control)?.Tag as string;
            var creator = new ImageCreatorWindow(vm.Console, kind == "icon", vm.GameName);
            string result = await creator.ShowDialog<string>(this);
            if (result != null)
                SetImage(kind, result);
        }

        private async void OnBrowseSound(object sender, RoutedEventArgs e)
        {
            string path = await Dialogs.PickFile(this, "Choose a boot sound", "Sound", "*.mp3", "*.wav", "*.btsnd", "*.m4a", "*.aiff");
            if (path != null)
                vm.BootSoundPath = path;
        }

        private void StopSound()
        {
            try
            {
                if (soundPlayer != null && !soundPlayer.HasExited)
                    soundPlayer.Kill();
            }
            catch
            {
            }
            soundPlayer = null;
        }

        private async void OnPlaySound(object sender, RoutedEventArgs e)
        {
            StopSound();
            if (!AppPaths.IsMac || !File.Exists(vm.BootSoundPath))
                return;
            try
            {
                string play = vm.BootSoundPath;
                if (play.EndsWith(".btsnd", StringComparison.OrdinalIgnoreCase))
                {
                    // afplay can't read btsnd: wrap its samples in a WAV first.
                    byte[] data = await File.ReadAllBytesAsync(play);
                    play = Path.Combine(Path.GetTempPath(), "uwuvci-preview.wav");
                    using var stream = new FileStream(play, FileMode.Create);
                    using var writer = new BinaryWriter(stream);
                    int length = Math.Max(0, data.Length - 8) & ~1;
                    writer.Write("RIFF"u8); writer.Write(36 + length); writer.Write("WAVEfmt "u8);
                    writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(48000 * 4); writer.Write((short)4); writer.Write((short)16);
                    writer.Write("data"u8); writer.Write(length);
                    for (int i = 8; i + 1 < 8 + length; i += 2)
                    {
                        writer.Write(data[i + 1]);
                        writer.Write(data[i]);
                    }
                }
                // Six seconds is all the console plays.
                soundPlayer = Process.Start(new ProcessStartInfo("/usr/bin/afplay") { ArgumentList = { "-t", "6", play }, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Logger.Log("Could not play the boot sound: " + ex.Message);
            }
        }

        private async void OnBrowseIni(object sender, RoutedEventArgs e)
        {
            string path = await Dialogs.PickFile(this, "Choose an INI file", "N64 VC configuration", "*.ini");
            if (path != null)
                vm.IniPath = path;
        }

        private async void OnBrowseGct(object sender, RoutedEventArgs e)
        {
            List<string> paths = await Dialogs.PickFiles(this, "Choose cheat code files", "GCT or TXT files", "*.gct", "*.txt");
            if (paths.Count == 0)
                return;
            var all = vm.GctPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Concat(paths).Distinct();
            vm.GctPaths = string.Join("\n", all);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = vm.IsInject && !vm.IsBusy && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (!vm.IsInject || vm.IsBusy)
                return;
            string path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (path == null)
                return;

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".uwuvci")
                await ImportConfigAsync(path);
            else if (Dialogs.ImagePatterns.Contains("*" + extension))
            {
                // Guess which image it is from its size.
                try
                {
                    using SkiaSharp.SKBitmap bitmap = ImageTools.Load(path);
                    if (bitmap.Width == bitmap.Height) vm.IconPath = path;
                    else if (bitmap.Width <= 200) vm.LogoPath = path;
                    else vm.TvPath = path;
                }
                catch (Exception ex)
                {
                    await Dialogs.Error(this, "Could not read the image", ex.Message);
                }
            }
            else if (extension == ".mp3" || extension == ".wav" || extension == ".btsnd")
                vm.BootSoundPath = path;
            else if (extension == ".ini" && vm.IsN64)
                vm.IniPath = path;
            else if (extension == ".gct" && vm.IsWii)
                vm.GctPaths = string.Join("\n", vm.GctPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries).Append(path).Distinct());
            else
                await SetRomAsync(path);
        }

        // ---- configs ------------------------------------------------------------------------------------------------

        private async void OnImportConfig(object sender, RoutedEventArgs e)
        {
            string path = await Dialogs.PickFile(this, "Import a config", "UWUVCI config", "*.uwuvci");
            if (path != null)
                await ImportConfigAsync(path);
        }

        private async Task ImportConfigAsync(string path)
        {
            GameConfig config;
            try
            {
                config = ConfigFile.Read(path);
            }
            catch (Exception ex)
            {
                await Dialogs.Error(this, "Import failed", "The config could not be read.\n" + ex.Message);
                return;
            }

            // GameCube configs are stored as GCN by the exporter, as WII by the injector.
            GameConsoles wanted = vm.Console;
            bool matches = config.Console == wanted || (wanted == GameConsoles.GCN && config.Console == GameConsoles.WII && config.fourbythree);
            if (!matches)
            {
                await Dialogs.Error(this, "Import failed", $"The config you are trying to import is made for {config.Console} injects, not for {vm.SelectedConsole.Name}.\nSwitch to that console and import it there.");
                return;
            }

            vm.ApplyConfig(config);
            await Dialogs.Message(this, "Import complete", "The config was imported. Select the ROM and check the base before injecting.");
        }

        private async void OnExportConfig(object sender, RoutedEventArgs e)
        {
            try
            {
                (GameConfig config, InjectContext _) = vm.BuildInject();
                if (vm.Console == GameConsoles.GCN)
                    config.Console = GameConsoles.GCN;
                string file = ConfigFile.Export(config, vm.BootSoundPath);
                if (await Dialogs.Message(this, "Export success", "The config was exported with its images embedded.\n" + file, "OK", "Show in Finder") == "Show in Finder")
                    Dialogs.Reveal(file);
            }
            catch (Exception ex)
            {
                await Dialogs.Error(this, "Export failed", ex.Message);
            }
        }

        // ---- inject -------------------------------------------------------------------------------------------------

        private async void OnInject(object sender, RoutedEventArgs e)
        {
            StopSound();
            (GameConfig config, InjectContext context) = vm.BuildInject();
            string rom = vm.RomPath;
            bool force43 = vm.Force43;
            string consoleName = vm.Console.ToString();
            bool isWiiFamily = vm.Console == GameConsoles.WII || vm.Console == GameConsoles.GCN;

            void Forward(IProgress<(int, string)> progress) =>
                context.ProgressChanged += (percent, message) => progress.Report((percent, message));

            bool injected = await RunBusy("Injecting game – please wait", progress =>
            {
                Forward(progress);
                Injection.Inject(config, rom, context, force43);
                return Task.CompletedTask;
            }, "Injection failed");
            if (!injected)
                return;

            string warnings = context.Warnings.Count == 0 ? "" : "\n\nNote:\n• " + string.Join("\n• ", context.Warnings);
            string choice = await Dialogs.Message(this, "Injection finished",
                "Choose how you want to export the inject.\n\n" +
                "• Installable (WUP): install it with WUP Installer GX2 or similar. Needs the Wii U Common Key.\n" +
                "• Loadiine: a plain folder for Loadiine." + warnings,
                "Installable (WUP)", "Loadiine", "Cancel");

            if (choice == "Installable (WUP)" && !BaseManager.IsCommonKeySet)
            {
                string key = await Dialogs.Prompt(this, "Wii U Common Key", "An installable inject is encrypted with the Wii U Common Key. Enter it to continue (32 hexadecimal characters).", "32 characters");
                if (key == null || !BaseManager.TrySetCommonKey(key))
                {
                    if (key != null)
                        await Dialogs.Error(this, "Wrong key", "That is not the Wii U Common Key. The inject was exported for Loadiine instead.");
                    choice = key == null ? "Cancel" : "Loadiine";
                }
                vm.RefreshBaseState();
            }

            if (choice != "Installable (WUP)" && choice != "Loadiine")
            {
                Injection.Clean();
                return;
            }

            string output = null;
            bool loadiine = choice == "Loadiine";
            bool exported = await RunBusy(loadiine ? "Copying inject" : "Packing inject – please wait", progress =>
            {
                Forward(progress);
                output = loadiine ? Injection.Loadiine(config.GameName, consoleName, context) : Injection.Packing(config.GameName, consoleName, context);
                return Task.CompletedTask;
            }, "Export failed", indeterminate: loadiine);
            if (!exported)
            {
                Injection.Clean();
                return;
            }

            string extra = "";
            if (vm.Console == GameConsoles.WII)
            {
                extra = "\nSome games cannot reboot into the Wii U Menu. Shut down via the GamePad.\nIf stuck on a black screen, you need to unplug your Wii U.";
                if (rom.ToLowerInvariant().EndsWith(".wad"))
                    extra += "\nMake sure that the chosen WAD is installed in your vWii!";
            }
            if (vm.Console == GameConsoles.GCN)
                extra = "\nMake sure to have Nintendont and its config on your SD card.";

            string done = await Dialogs.Message(this, "Injection complete",
                (loadiine ? "" : "You need CFW (e.g. Haxchi, Mocha, Tiramisu or Aroma) to install and run this inject.\nIt's recommended to install onto USB to avoid brick risks." + extra + "\n\n") +
                "Saved to:\n" + output,
                "Show in Finder", "OK");
            if (done == "Show in Finder")
                Dialogs.Reveal(output);
        }

        // ---- settings -----------------------------------------------------------------------------------------------

        private async void OnResetTitleKeys(object sender, RoutedEventArgs e)
        {
            if (await Dialogs.Message(this, "Resetting Title Keys", "This will forget all entered Title Keys, meaning you will need to enter them again.\nDo you still wish to continue?", "Reset", "Cancel") != "Reset")
                return;
            BaseManager.ResetTitleKeys();
            vm.RefreshBaseState();
        }

        private void OnShowOutPath(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(vm.OutPath);
            Dialogs.Reveal(vm.OutPath);
        }

        private void OnShowBasePath(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(vm.BasePath);
            Dialogs.Reveal(vm.BasePath);
        }

        private void OnShowDataPath(object sender, RoutedEventArgs e) => Dialogs.Reveal(vm.DataPath);

        private async void OnChangeOutPath(object sender, RoutedEventArgs e)
        {
            string folder = await Dialogs.PickFolder(this, "Choose where injected games are saved");
            if (folder != null)
                vm.SetOutPath(folder);
        }

        private async void OnChangeBasePath(object sender, RoutedEventArgs e)
        {
            string folder = await Dialogs.PickFolder(this, "Choose where downloaded bases are kept");
            if (folder != null)
                vm.SetBasePath(folder);
        }

        private async void OnUpdateTools(object sender, RoutedEventArgs e)
        {
            if (await RunBusy("Updating tools", progress => ToolManager.UpdateAllAsync(progress), "The tools could not be updated"))
                await Dialogs.Message(this, "Finished update", "Finished updating the tool files.");
        }

        private async void OnUpdateBases(object sender, RoutedEventArgs e)
        {
            if (await RunBusy("Updating base lists", progress => BaseManager.DownloadListsAsync(true, progress), "The base lists could not be updated"))
                await Dialogs.Message(this, "Finished updating", "Finished updating the base lists.");
            vm.ReloadBases();
        }

        private void OnOpenGuides(object sender, RoutedEventArgs e) => Dialogs.OpenUrl("https://uwuvci-prime.github.io/UWUVCI-Resources/");
    }
}
