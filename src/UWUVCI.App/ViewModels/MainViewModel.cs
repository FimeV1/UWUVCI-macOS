using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameBaseClassLibrary;
using UWUVCI.Core;
using UWUVCI.Core.Engine;
using UWUVCI.Core.Services;
using UWUVCI.Core.Tools;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.App.ViewModels
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(params string[] names)
        {
            foreach (string name in names)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class ConsoleItem
    {
        public string Name { get; set; }
        public string Short { get; set; }
        public GameConsoles Console { get; set; }
        public Bitmap Icon { get; set; }
        public bool IsSettings { get; set; }
        public string[] RomPatterns { get; set; }
        public string RomHint { get; set; }
    }

    public sealed class BaseItem
    {
        public GameBases Base { get; set; }
        public string Name { get; set; }
        public bool IsCustom => Base.Name == "Custom";
        public override string ToString() => Name;
    }

    /// <summary>State of the main window: the inject being put together, and the settings page.</summary>
    public sealed class MainViewModel : Observable
    {
        public static readonly string[] GamePadModes =
        {
            "Do not use. Wii Remotes only",
            "Classic Controller",
            "Horizontal Wii Remote",
            "Vertical Wii Remote",
            "Force Classic Controller",
            "Force No Classic Controller",
        };

        public IReadOnlyList<ConsoleItem> Consoles { get; }
        public ObservableCollection<BaseItem> Bases { get; } = new ObservableCollection<BaseItem>();
        public IReadOnlyList<string> GamePadModeList => GamePadModes;
        public IReadOnlyList<string> RegionModes { get; } = new[] { "Keep original", "To PAL", "To NTSC (USA)", "To JPN" };

        public MainViewModel()
        {
            ConsoleItem C(string name, string shortName, GameConsoles console, string asset, string hint, params string[] patterns) => new ConsoleItem
            {
                Name = name, Short = shortName, Console = console, RomHint = hint, RomPatterns = patterns,
                Icon = new Bitmap(AssetLoader.Open(new Uri($"avares://UWUVCI/Assets/{asset}.png"))),
            };

            Consoles = new List<ConsoleItem>
            {
                C("Nintendo DS", "NDS", GameConsoles.NDS, "nds", "Nintendo DS ROM (.nds, .srl)", "*.nds", "*.srl"),
                C("Game Boy Advance", "GBA", GameConsoles.GBA, "gba", "Game Boy series ROM (.gba, .gbc, .gb)", "*.gba", "*.gbc", "*.gb"),
                C("Nintendo 64", "N64", GameConsoles.N64, "n64", "Nintendo 64 ROM (.z64, .v64, .n64)", "*.z64", "*.v64", "*.n64"),
                C("Super Nintendo", "SNES", GameConsoles.SNES, "snes", "SNES ROM (.sfc, .smc)", "*.sfc", "*.smc"),
                C("NES", "NES", GameConsoles.NES, "nes", "NES ROM (.nes)", "*.nes"),
                C("TurboGrafx-16", "TG16", GameConsoles.TG16, "tg16", "TurboGrafx-16 ROM (.pce)", "*.pce"),
                C("MSX", "MSX", GameConsoles.MSX, "msx", "MSX / MSX2 ROM (.rom)", "*.rom"),
                C("Wii", "WII", GameConsoles.WII, "wii", "Wii game, homebrew or channel (.iso, .wbfs, .nkit.iso, .dol, .wad)", "*.iso", "*.wbfs", "*.dol", "*.wad"),
                C("GameCube", "GCN", GameConsoles.GCN, "gcn", "GameCube game (.iso, .gcm, .nkit.iso, .gcz)", "*.iso", "*.gcm", "*.gcz"),
                new ConsoleItem { Name = "Settings", IsSettings = true },
            };
            selectedConsole = Consoles[0];
            ReloadBases();
        }

        // ---- navigation ---------------------------------------------------------------------------------------------

        private ConsoleItem selectedConsole;
        public ConsoleItem SelectedConsole
        {
            get => selectedConsole;
            set
            {
                if (value == null || !Set(ref selectedConsole, value))
                    return;
                if (!value.IsSettings)
                {
                    ResetInject();
                    ReloadBases();
                }
                Raise(nameof(IsSettings), nameof(IsInject), nameof(Title), nameof(RomHint),
                    nameof(IsWii), nameof(IsGcn), nameof(IsN64), nameof(IsGba), nameof(IsNds), nameof(IsTg16), nameof(IsNesSnes), nameof(HasOptions));
            }
        }

        public bool IsSettings => selectedConsole.IsSettings;
        public bool IsInject => !selectedConsole.IsSettings;
        public string Title => selectedConsole.IsSettings ? "Settings" : selectedConsole.Name + " inject";
        public string RomHint => TurboCd ? "Folder with the game's .hcd, .ogg and .bin files" : selectedConsole.RomHint;
        public GameConsoles Console => selectedConsole.Console;

        public bool IsWii => IsInject && Console == GameConsoles.WII;
        public bool IsGcn => IsInject && Console == GameConsoles.GCN;
        public bool IsN64 => IsInject && Console == GameConsoles.N64;
        public bool IsGba => IsInject && Console == GameConsoles.GBA;
        public bool IsNds => IsInject && Console == GameConsoles.NDS;
        public bool IsTg16 => IsInject && Console == GameConsoles.TG16;
        public bool IsNesSnes => IsInject && (Console == GameConsoles.NES || Console == GameConsoles.SNES);
        public bool HasOptions => IsWii || IsGcn || IsN64 || IsGba || IsNds || IsTg16;

        // ---- base ---------------------------------------------------------------------------------------------------

        private BaseItem selectedBase;
        public BaseItem SelectedBase
        {
            get => selectedBase;
            set
            {
                if (Set(ref selectedBase, value))
                    RefreshBaseState();
            }
        }

        private string customBasePath;
        public string CustomBasePath
        {
            get => customBasePath;
            set { if (Set(ref customBasePath, value)) RefreshBaseState(); }
        }

        public void ReloadBases()
        {
            if (selectedConsole.IsSettings)
                return;
            string previous = selectedBase?.Name;
            Bases.Clear();
            Bases.Add(new BaseItem { Base = BaseManager.CustomBase(), Name = "Custom base (your own dump)" });
            foreach (GameBases b in BaseManager.GetBases(Console))
                Bases.Add(new BaseItem { Base = b, Name = BaseManager.DisplayName(b) });

            // Prefer a base that is ready to use, then the first real one.
            SelectedBase = Bases.FirstOrDefault(b => b.Name == previous)
                ?? Bases.FirstOrDefault(b => !b.IsCustom && BaseManager.IsDownloaded(b.Base))
                ?? Bases.Skip(1).FirstOrDefault() ?? Bases[0];
        }

        public bool BaseIsCustom => selectedBase?.IsCustom == true;
        public bool BaseIsListed => selectedBase != null && !selectedBase.IsCustom;
        public bool BaseDownloaded => BaseIsListed && BaseManager.IsDownloaded(selectedBase.Base);
        public bool TitleKeySet => BaseIsListed && BaseManager.IsTitleKeySet(Console, selectedBase.Base);
        public bool CommonKeySet => BaseManager.IsCommonKeySet;
        public bool NeedsTitleKey => BaseIsListed && !BaseDownloaded && !TitleKeySet;
        public bool NeedsCommonKey => BaseIsListed && !BaseDownloaded && !CommonKeySet;
        public bool CanDownloadBase => BaseIsListed && !BaseDownloaded && TitleKeySet && CommonKeySet;

        public bool BaseReady =>
            BaseDownloaded || (BaseIsCustom && BaseManager.InspectCustomBase(customBasePath) == BaseManager.CustomBaseKind.Loadiine);

        public string BaseStatus
        {
            get
            {
                if (selectedBase == null)
                    return "";
                if (BaseIsCustom)
                {
                    if (string.IsNullOrEmpty(customBasePath))
                        return "Choose the folder of a base you dumped from your Wii U (code / content / meta, or NUS format).";
                    return BaseReady ? "✓ Custom base ready." : "This folder is not a usable base.";
                }
                if (BaseDownloaded)
                    return "✓ Base downloaded and ready.";
                if (NeedsCommonKey && NeedsTitleKey)
                    return "To download this base, enter the Wii U Common Key and this game's Title Key.";
                if (NeedsCommonKey)
                    return "To download this base, enter the Wii U Common Key.";
                if (NeedsTitleKey)
                    return "To download this base, enter this game's Title Key.";
                return "Keys entered. The base can be downloaded now.";
            }
        }

        public void RefreshBaseState() =>
            Raise(nameof(BaseIsCustom), nameof(BaseIsListed), nameof(BaseDownloaded), nameof(TitleKeySet), nameof(CommonKeySet), nameof(NeedsTitleKey),
                nameof(NeedsCommonKey), nameof(CanDownloadBase), nameof(BaseReady), nameof(BaseStatus), nameof(CanInject), nameof(CommonKeyStatus));

        // ---- the inject ---------------------------------------------------------------------------------------------

        private string romPath = "", gameName = "", iconPath = "", tvPath = "", drcPath = "", logoPath = "", bootSoundPath = "", iniPath = "", disc2Path = "", gctPaths = "";

        public string RomPath { get => romPath; set { if (Set(ref romPath, value ?? "")) Raise(nameof(CanInject), nameof(RomIsDisc)); } }
        public string GameName { get => gameName; set => Set(ref gameName, value ?? ""); }
        public string IconPath { get => iconPath; set { if (Set(ref iconPath, value ?? "")) IconPreview = Preview(iconPath); } }
        public string TvPath { get => tvPath; set { if (Set(ref tvPath, value ?? "")) TvPreview = Preview(tvPath); } }
        public string DrcPath { get => drcPath; set { if (Set(ref drcPath, value ?? "")) DrcPreview = Preview(drcPath); } }
        public string LogoPath { get => logoPath; set { if (Set(ref logoPath, value ?? "")) LogoPreview = Preview(logoPath); } }
        public string BootSoundPath { get => bootSoundPath; set { if (Set(ref bootSoundPath, value ?? "")) Raise(nameof(HasBootSound)); } }
        public string IniPath { get => iniPath; set => Set(ref iniPath, value ?? ""); }
        public string Disc2Path { get => disc2Path; set => Set(ref disc2Path, value ?? ""); }
        public string GctPaths { get => gctPaths; set => Set(ref gctPaths, value ?? ""); }
        public bool HasBootSound => File.Exists(bootSoundPath);

        private Bitmap iconPreview, tvPreview, drcPreview, logoPreview;
        public Bitmap IconPreview { get => iconPreview; private set => Set(ref iconPreview, value); }
        public Bitmap TvPreview { get => tvPreview; private set => Set(ref tvPreview, value); }
        public Bitmap DrcPreview { get => drcPreview; private set => Set(ref drcPreview, value); }
        public Bitmap LogoPreview { get => logoPreview; private set => Set(ref logoPreview, value); }

        /// <summary>Loads any supported image (including TGA) as a thumbnail; null when it can't be read.</summary>
        public static Bitmap Preview(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                using SkiaSharp.SKBitmap bitmap = ImageTools.Load(path);
                using SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using SkiaSharp.SKData data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                using var stream = new MemoryStream(data.ToArray());
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not preview {path}: {ex.Message}");
                return null;
            }
        }

        public bool CanInject => BaseReady && (File.Exists(romPath) || (TurboCd && Directory.Exists(romPath)));

        /// <summary>True when the Wii ROM is a disc image (not a .dol or .wad), which is what most options apply to.</summary>
        public bool RomIsDisc
        {
            get
            {
                string lower = romPath.ToLowerInvariant();
                return !lower.EndsWith(".dol") && !lower.EndsWith(".wad");
            }
        }

        // Options. Names follow the Windows app where one exists.
        private int gamePadMode, regionMode, brightness = 80, pixelArtUpscaler;
        private bool swapLR, disablePassthrough, dontTrim, removeDeflicker, removeDithering, halfVFilter;
        private bool force43, disableGamePad, n64DarkFilter = true, n64WideScreen, gbaDarkFilter = true, pokePatch, dsLayout, dsPhantomHourglass, turboCd;

        public int GamePadMode
        {
            get => gamePadMode;
            set
            {
                if (!Set(ref gamePadMode, Math.Max(0, value)))
                    return;
                if (!CanSwapLR)
                    SwapLR = false;
                Raise(nameof(CanSwapLR));
            }
        }
        public bool CanSwapLR => gamePadMode == 1 || gamePadMode == 4;
        public bool SwapLR { get => swapLR; set => Set(ref swapLR, value); }
        public bool DisablePassthrough { get => disablePassthrough; set => Set(ref disablePassthrough, value); }
        public bool DontTrim { get => dontTrim; set => Set(ref dontTrim, value); }
        public bool RemoveDeflicker { get => removeDeflicker; set => Set(ref removeDeflicker, value); }
        public bool RemoveDithering { get => removeDithering; set => Set(ref removeDithering, value); }
        public bool HalfVFilter { get => halfVFilter; set => Set(ref halfVFilter, value); }
        public int RegionMode { get => regionMode; set => Set(ref regionMode, Math.Max(0, value)); }
        public bool Force43 { get => force43; set => Set(ref force43, value); }
        public bool DisableGamePad { get => disableGamePad; set => Set(ref disableGamePad, value); }
        public bool N64DarkFilter { get => n64DarkFilter; set => Set(ref n64DarkFilter, value); }
        public bool N64WideScreen { get => n64WideScreen; set => Set(ref n64WideScreen, value); }
        public bool GbaDarkFilter { get => gbaDarkFilter; set => Set(ref gbaDarkFilter, value); }
        public bool PokePatch { get => pokePatch; set => Set(ref pokePatch, value); }
        public bool DsLayout { get => dsLayout; set => Set(ref dsLayout, value); }
        public bool DsPhantomHourglass { get => dsPhantomHourglass; set => Set(ref dsPhantomHourglass, value); }
        public int Brightness { get => brightness; set => Set(ref brightness, Math.Clamp(value, 0, 100)); }
        public int PixelArtUpscaler { get => pixelArtUpscaler; set => Set(ref pixelArtUpscaler, Math.Clamp(value, 0, 256)); }
        public bool TurboCd
        {
            get => turboCd;
            set { if (Set(ref turboCd, value)) { RomPath = ""; Raise(nameof(RomHint), nameof(CanInject)); } }
        }

        public void ResetInject()
        {
            RomPath = GameName = IconPath = TvPath = DrcPath = LogoPath = BootSoundPath = IniPath = Disc2Path = GctPaths = "";
            CustomBasePath = null;
            GamePadMode = 0; RegionMode = 0;
            SwapLR = DisablePassthrough = DontTrim = RemoveDeflicker = RemoveDithering = HalfVFilter = false;
            Force43 = DisableGamePad = N64WideScreen = PokePatch = DsLayout = DsPhantomHourglass = false;
            N64DarkFilter = GbaDarkFilter = true;
            Brightness = 80; PixelArtUpscaler = 0;
            turboCd = false;
            Raise(nameof(TurboCd));
        }

        /// <summary>The settings as the engine wants them.</summary>
        public (GameConfig, InjectContext) BuildInject()
        {
            bool gc = Console == GameConsoles.GCN;
            var config = new GameConfig
            {
                Console = gc ? GameConsoles.WII : Console,
                BaseRom = selectedBase.Base,
                CBasePath = BaseIsCustom ? customBasePath : null,
                GameName = gameName.Trim(),
                donttrim = dontTrim,
                lr = swapLR,
                motepass = !disablePassthrough,
                pokepatch = pokePatch,
                tgcd = turboCd,
                fourbythree = force43,
                disgamepad = disableGamePad,
                Index = gc ? (disableGamePad ? -1 : 1) : gamePadMode,
                rf = regionMode != 0,
                rfus = regionMode == 2,
                rfjp = regionMode == 3,
            };
            string Path(string p) => File.Exists(p) ? p : null;
            config.TGAIco.ImgPath = Path(iconPath);
            config.TGATv.ImgPath = Path(tvPath);
            config.TGADrc.ImgPath = Path(drcPath);
            config.TGALog.ImgPath = Path(logoPath);
            config.N64Stuff.INIPath = Path(iniPath);
            // "DarkFilter" on the N64 config means "remove it"; on the GBA config it means "keep it". Both as in the Windows app.
            config.N64Stuff.DarkFilter = !n64DarkFilter;
            config.N64Stuff.WideScreen = n64WideScreen;
            config.GBAStuff.DarkFilter = gbaDarkFilter;

            var context = new InjectContext
            {
                GC = gc,
                Index = config.Index,
                LR = swapLR,
                passtrough = !disablePassthrough,
                donttrim = dontTrim,
                NKITFLAG = romPath.ToLowerInvariant().Contains("nkit"),
                regionfrii = config.rf,
                regionfriius = config.rfus,
                regionfriijp = config.rfjp,
                RemoveDeflicker = removeDeflicker,
                RemoveDithering = removeDithering,
                HalfVFilter = halfVFilter,
                GctPath = gctPaths,
                gc2rom = disc2Path,
                PokePatch = pokePatch,
                DSLayout = dsLayout,
                STLayout = dsPhantomHourglass,
                Brightness = brightness,
                PixelArtUpscaler = pixelArtUpscaler,
                BootSound = bootSoundPath,
            };
            return (config, context);
        }

        /// <summary>Applies an imported .uwuvci config. Embedded images are written to the cache folder first.</summary>
        public void ApplyConfig(GameConfig config)
        {
            string Extract(UWUVCI_AIO_WPF.Models.PNGTGA image, string name)
            {
                if (image?.ImgBin != null && image.ImgBin.Length > 0)
                {
                    Directory.CreateDirectory(AppPaths.Repo);
                    string path = System.IO.Path.Combine(AppPaths.Repo, $"config-{name}.{(string.IsNullOrEmpty(image.extension) ? "png" : image.extension)}");
                    File.WriteAllBytes(path, image.ImgBin);
                    return path;
                }
                return File.Exists(image?.ImgPath ?? "") ? image.ImgPath : "";
            }

            GameName = config.GameName ?? "";
            IconPath = Extract(config.TGAIco, "iconTex");
            TvPath = Extract(config.TGATv, "bootTvTex");
            DrcPath = Extract(config.TGADrc, "bootDrcTex");
            LogoPath = Extract(config.TGALog, "bootLogoTex");
            BootSoundPath = ConfigFile.ExtractBootSound(config) ?? "";

            if (config.N64Stuff?.INIBin != null && config.N64Stuff.INIBin.Length > 0)
            {
                Directory.CreateDirectory(AppPaths.Repo);
                string ini = System.IO.Path.Combine(AppPaths.Repo, "config-game.ini");
                File.WriteAllBytes(ini, config.N64Stuff.INIBin);
                IniPath = ini;
            }
            else
                IniPath = File.Exists(config.N64Stuff?.INIPath ?? "") ? config.N64Stuff.INIPath : "";

            N64DarkFilter = !(config.N64Stuff?.DarkFilter ?? false);
            N64WideScreen = config.N64Stuff?.WideScreen ?? false;
            GbaDarkFilter = config.GBAStuff?.DarkFilter ?? true;
            DontTrim = config.donttrim;
            SwapLR = config.lr;
            DisablePassthrough = !config.motepass;
            PokePatch = config.pokepatch;
            Force43 = config.fourbythree;
            DisableGamePad = config.disgamepad;
            if (Console == GameConsoles.WII)
                GamePadMode = Math.Clamp(config.Index, 0, GamePadModes.Length - 1);
            RegionMode = !config.rf ? 0 : config.rfus ? 2 : config.rfjp ? 3 : 1;
            turboCd = config.tgcd;
            Raise(nameof(TurboCd), nameof(RomHint));
        }

        // ---- busy overlay -------------------------------------------------------------------------------------------

        private bool isBusy;
        private string busyTitle = "", busyMessage = "";
        private int busyProgress;
        private bool busyIndeterminate;

        public bool IsBusy { get => isBusy; set => Set(ref isBusy, value); }
        public string BusyTitle { get => busyTitle; set => Set(ref busyTitle, value); }
        public string BusyMessage { get => busyMessage; set => Set(ref busyMessage, value); }
        public int BusyProgress { get => busyProgress; set => Set(ref busyProgress, value); }
        public bool BusyIndeterminate { get => busyIndeterminate; set => Set(ref busyIndeterminate, value); }

        // ---- settings -----------------------------------------------------------------------------------------------

        public string CommonKeyStatus => CommonKeySet ? "✓ Common Key entered" : "Not entered yet";
        public string BasePath => JsonSettingsManager.Settings.BasePath;
        public string OutPath => JsonSettingsManager.Settings.OutPath;
        public string DataPath => AppPaths.DataRoot;
        public string VersionText => "UWUVCI for macOS 1.0 · based on UWUVCI AIO V3.N2";

        public void SetBasePath(string path)
        {
            JsonSettingsManager.Settings.BasePath = path;
            JsonSettingsManager.SaveSettings();
            Raise(nameof(BasePath));
            RefreshBaseState();
        }

        public void SetOutPath(string path)
        {
            JsonSettingsManager.Settings.OutPath = path;
            JsonSettingsManager.SaveSettings();
            Raise(nameof(OutPath));
        }
    }
}
