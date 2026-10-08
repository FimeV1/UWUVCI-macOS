using GameBaseClassLibrary;
using System;
using UWUVCI_AIO_WPF.Models;

// Namespaces and field names in this file are part of the .uwuvci / .vck file formats (BinaryFormatter),
// which are shared with the Windows app. Do not rename.
namespace UWUVCI_AIO_WPF
{
    [Serializable]
    public class GameConfig
    {
        public GameConfig Clone()
        {
            return MemberwiseClone() as GameConfig;
        }
        public GameConsoles Console { get; set; }
        public GameBases BaseRom { get; set; }

        private string cBasePath;

        public string CBasePath
        {
            get { return cBasePath; }
            set { cBasePath = value; }
        }

        public byte[] bootsound;
        public string extension = "";
        public bool fourbythree = false;
        public bool disgamepad = false;
        public bool donttrim = false;
        public bool lr = false;
        public bool motepass = false;
        public bool jppatch = false;
        public bool pokepatch = false;

        public bool tgcd = false;

        public int Index;

        public bool pixelperfect = false;
        public string GameName { get; set; }

        public bool vm = false;
        public bool vmtopal = false;

        public bool rf = false;
        public bool rfus = false;
        public bool rfjp = false;

        public PNGTGA TGAIco { get; set; } = new PNGTGA();
        public PNGTGA TGADrc { get; set; } = new PNGTGA();
        public PNGTGA TGATv { get; set; } = new PNGTGA();
        public PNGTGA TGALog { get; set; } = new PNGTGA();
        public N64Conf N64Stuff { get; set; } = new N64Conf();
        public N64Conf GBAStuff { get; set; } = new N64Conf();
    }
}

namespace UWUVCI_AIO_WPF.Models
{
    [Serializable]
    public class N64Conf
    {
        private string iniPath = null;

        public string INIPath
        {
            get { return iniPath; }
            set { iniPath = value; }
        }
        private bool darkFilter = false;

        public bool DarkFilter
        {
            get { return darkFilter; }
            set { darkFilter = value; }
        }

        private bool wideScreen = false;

        public bool WideScreen
        {
            get { return wideScreen; }
            set { wideScreen = value; }
        }

        public byte[] INIBin { get; set; }
    }

    [Serializable]
    public class PNGTGA
    {
        private string imgPath = null;

        public string ImgPath
        {
            get { return imgPath; }
            set { imgPath = value; }
        }

        public byte[] ImgBin { get; set; } = null;

        public string extension { get; set; }
    }

    [Serializable]
    public class TKeys
    {
        public GameBases Base { get; set; }
        public string Tkey { get; set; } = null;
    }

    public class JsonAppSettings
    {
        public bool PathsSet { get; set; } = false;
        public string BasePath { get; set; } = "";
        public string OutPath { get; set; } = "";
        public string Ckey { get; set; } = "";
        public bool SetBaseOnce { get; set; } = false;
        public bool SetOutOnce { get; set; } = false;
        public bool UpgradeRequired { get; set; } = true;
        public string SysKey { get; set; } = "";
        public string SysKey1 { get; set; } = "";
        public bool dont { get; set; } = false;
        public bool ndsw { get; set; } = false;
        public bool snesw { get; set; } = false;
        public bool gczw { get; set; } = false;
        public string Ancast { get; set; } = "";
        public int QuizScreen { get; set; } = 0;
    }
}
