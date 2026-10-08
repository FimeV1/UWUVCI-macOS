using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UWUVCI.Core;

namespace UWUVCI.App.Views
{
    /// <summary>Message boxes, prompts and file pickers.</summary>
    public static class Dialogs
    {
        private static Window Shell(string title, Control content)
        {
            return new Window
            {
                Title = title,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 380,
                MaxWidth = 620,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
        }

        /// <summary>Shows a message with the given buttons and returns the one pressed (null when the window is closed).</summary>
        public static async Task<string> Message(Window owner, string title, string text, params string[] buttons)
        {
            if (buttons.Length == 0)
                buttons = new[] { "OK" };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
            panel.Children.Add(row);

            Window window = Shell(title, panel);
            string result = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                string label = buttons[i];
                var button = new Button { Content = label, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
                // The first button is the default action.
                if (i == 0)
                {
                    button.Classes.Add("primary");
                    button.IsDefault = true;
                }
                if (i == buttons.Length - 1 && buttons.Length > 1)
                    button.IsCancel = true;
                button.Click += (s, e) => { result = label; window.Close(); };
                row.Children.Insert(0, button);
            }

            await window.ShowDialog(owner);
            return result;
        }

        public static Task Error(Window owner, string title, string text) => Message(owner, title, text, "OK");

        /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
        public static async Task<string> Prompt(Window owner, string title, string text, string watermark = "")
        {
            var box = new TextBox { Watermark = watermark, MinWidth = 440, FontFamily = new FontFamily("Menlo, Consolas, monospace") };
            var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            ok.Classes.Add("primary");
            var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(cancel);
            row.Children.Add(ok);

            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            panel.Children.Add(box);
            panel.Children.Add(row);

            Window window = Shell(title, panel);
            string result = null;
            ok.Click += (s, e) => { result = box.Text ?? ""; window.Close(); };
            cancel.Click += (s, e) => window.Close();
            window.Opened += (s, e) => box.Focus();
            await window.ShowDialog(owner);
            return result;
        }

        public static async Task<string> PickFile(Window owner, string title, string typeName, params string[] patterns)
        {
            var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
            if (patterns.Length > 0)
                options.FileTypeFilter = new[]
                {
                    new FilePickerFileType(typeName) { Patterns = patterns.SelectMany(p => new[] { p, p.ToUpperInvariant() }).Distinct().ToArray() },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                };
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        public static async Task<List<string>> PickFiles(Window owner, string title, string typeName, params string[] patterns)
        {
            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns }, new FilePickerFileType("All files") { Patterns = new[] { "*" } } },
            };
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(options);
            return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToList();
        }

        public static async Task<string> PickFolder(Window owner, string title)
        {
            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }

        public static readonly string[] ImagePatterns = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" };

        /// <summary>Opens a folder (or reveals a file) in Finder / Explorer.</summary>
        public static void Reveal(string path)
        {
            try
            {
                if (AppPaths.IsMac)
                    Process.Start("/usr/bin/open", File.Exists(path) ? new[] { "-R", path } : new[] { path });
                else
                    Process.Start(new ProcessStartInfo { FileName = Directory.Exists(path) ? path : Path.GetDirectoryName(path), UseShellExecute = true });
            }
            catch
            {
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (AppPaths.IsMac)
                    Process.Start("/usr/bin/open", new[] { url });
                else
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
