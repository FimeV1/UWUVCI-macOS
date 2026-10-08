using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GameBaseClassLibrary;
using UWUVCI.App.ViewModels;
using UWUVCI.App.Views;
using UWUVCI.Core;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Shots
{
    public static class Program
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<UWUVCI.App.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont();

        private static string output;

        private static void Shot(Window window, string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Save(Path.Combine(output, name + ".png"));
            Console.WriteLine("wrote " + name + ".png");
        }

        public static int Main(string[] args)
        {
            output = Path.GetFullPath(args.Length > 0 ? args[0] : "shots");
            Directory.CreateDirectory(output);
            AppPaths.EnsureCreated();
            JsonSettingsManager.LoadSettings();

            using var session = HeadlessUnitTestSession.StartNew(typeof(Program));
            session.Dispatch(() =>
            {
                var window = new MainWindow();
                window.Show();
                var vm = (MainViewModel)window.DataContext;
                Shot(window, "01-nds");

                foreach (GameConsoles console in new[] { GameConsoles.WII, GameConsoles.GCN, GameConsoles.N64, GameConsoles.GBA })
                {
                    vm.SelectedConsole = vm.Consoles.First(c => !c.IsSettings && c.Console == console);
                    if (args.Length > 1 && File.Exists(args[1]))
                    {
                        vm.TvPath = args[1];
                        vm.IconPath = args[1];
                    }
                    vm.GameName = "Example|Game";
                    Shot(window, "02-" + console.ToString().ToLower());
                }

                vm.SelectedConsole = vm.Consoles.First(c => c.IsSettings);
                Shot(window, "03-settings");

                vm.SelectedConsole = vm.Consoles[0];
                vm.BusyTitle = "Injecting game – please wait";
                vm.BusyMessage = "Creating ISO from trimmed ROM...";
                vm.BusyProgress = 50;
                vm.IsBusy = true;
                Shot(window, "04-busy");
                vm.IsBusy = false;

                var creator = new ImageCreatorWindow(GameConsoles.N64, false, "Example|Game");
                creator.Show();
                Shot(creator, "05-creator");
                creator.Close();
                window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
    }
}
