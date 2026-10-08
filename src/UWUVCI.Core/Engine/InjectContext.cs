using System;
using System.Collections.Generic;

namespace UWUVCI.Core.Engine
{
    /// <summary>
    /// Everything an injection needs besides the <see cref="UWUVCI_AIO_WPF.GameConfig"/>. In the Windows app these are
    /// loose fields on MainViewModel; the member names are kept so the two code bases stay easy to compare.
    /// </summary>
    public class InjectContext
    {
        public event Action<int, string> ProgressChanged;

        private int progress;
        private string message = "";

        public int Progress
        {
            get => progress;
            set { progress = value; ProgressChanged?.Invoke(progress, message); }
        }

        public string msg
        {
            get => message;
            set { message = value ?? ""; ProgressChanged?.Invoke(progress, message); }
        }

        /// <summary>Things that were skipped or substituted; shown to the user after a successful inject.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Skip the free disk space check.</summary>
        public bool saveworkaround;

        /// <summary>The Wii base is used for a GameCube game.</summary>
        public bool GC;

        // Wii
        /// <summary>GamePad mode: 0 none, 1 Classic Controller, 2 horizontal Wii Remote, 3 vertical Wii Remote, 4 force Classic Controller, 5 force no Classic Controller.</summary>
        public int Index = -1;
        public bool LR;
        public bool passtrough = true;
        public bool donttrim;
        public bool NKITFLAG;
        public bool regionfrii, regionfriius, regionfriijp;
        public bool RemoveDeflicker, RemoveDithering, HalfVFilter;
        /// <summary>Video mode patch (wii-vmc).</summary>
        public bool Patch;
        public bool toPal;
        /// <summary>One .gct / .txt cheat file per line.</summary>
        public string GctPath = "";

        // GameCube
        public string gc2rom = "";

        // NES / SNES
        public bool pixelperfect;

        // GBA
        public bool PokePatch;

        // NDS
        public bool DSLayout, STLayout;
        public int Brightness = 80;
        public int PixelArtUpscaler;

        public string BootSound = "";

        // Results
        public string prodcode = "";
        public string foldername = "";
    }

    /// <summary>An injection failure with a message meant for the user.</summary>
    public class InjectionException : Exception
    {
        public InjectionException(string message, Exception inner) : base(message, inner) { }
    }
}
