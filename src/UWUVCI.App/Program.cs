using System;
using System.IO;
using Avalonia;
using UWUVCI.Core;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                AppPaths.EnsureCreated();
                JsonSettingsManager.LoadSettings();
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => Logger.Log("Unhandled exception: " + e.ExceptionObject);
                Logger.Log($"UWUVCI for macOS starting ({Environment.OSVersion}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppPaths.DataRoot, "crash.txt"), ex.ToString()); } catch { }
                Logger.Log("Fatal: " + ex);
                return 1;
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
