using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GameBaseClassLibrary;
using SkiaSharp;
using UWUVCI.Core.Services;
using UWUVCI.Core.Tools;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.App.Views
{
    /// <summary>
    /// Makes a boot screen or menu icon in the Virtual Console style from a screenshot. Closes with the path of the
    /// saved image, or null when cancelled.
    /// </summary>
    public sealed class ImageCreatorWindow : Window
    {
        private readonly GameConsoles console;
        private readonly bool icon;
        private readonly List<FrameStyle> styles;

        private readonly ComboBox styleBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox line1 = new TextBox { Watermark = "Game name" };
        private readonly TextBox line2 = new TextBox { Watermark = "Second line (optional)" };
        private readonly TextBox year = new TextBox { Watermark = "e.g. 1998", Width = 110, MaxLength = 4 };
        private readonly TextBox players = new TextBox { Watermark = "1-4", Width = 110, MaxLength = 1 };
        private readonly TextBlock screenshotLabel = new TextBlock { Text = "No screenshot chosen", Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Image preview = new Image { Stretch = Stretch.Uniform };

        private SKBitmap screenshot;
        private SKBitmap current;

        public ImageCreatorWindow(GameConsoles console, bool icon, string gameName)
        {
            this.console = console;
            this.icon = icon;
            styles = BootImageComposer.StylesFor(console);

            Title = icon ? "Create menu icon" : "Create boot image";
            Width = icon ? 520 : 900;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            string[] names = (gameName ?? "").Split('|');
            line1.Text = names[0].Trim();
            line2.Text = names.Length > 1 ? names[1].Trim() : "";

            styleBox.ItemsSource = styles.Select(s => s.Name).ToList();
            styleBox.SelectedIndex = 0;

            var browse = new Button { Content = "Choose screenshot…" };
            browse.Click += async (s, e) =>
            {
                string path = await Dialogs.PickFile(this, "Choose a screenshot of the game", "Images", Dialogs.ImagePatterns);
                if (path == null)
                    return;
                try
                {
                    SKBitmap loaded = ImageTools.Load(path);
                    screenshot?.Dispose();
                    screenshot = loaded;
                    screenshotLabel.Text = Path.GetFileName(path);
                    Render();
                }
                catch (Exception ex)
                {
                    await Dialogs.Error(this, "Could not read the image", ex.Message);
                }
            };

            var form = new StackPanel { Spacing = 10, Width = icon ? double.NaN : 300 };
            Control Row(string label, Control control)
            {
                var panel = new StackPanel { Spacing = 4 };
                panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
                panel.Children.Add(control);
                return panel;
            }

            var screenshotRow = new DockPanel();
            DockPanel.SetDock(browse, Dock.Left);
            screenshotLabel.Margin = new Thickness(10, 0, 0, 0);
            screenshotRow.Children.Add(browse);
            screenshotRow.Children.Add(screenshotLabel);
            form.Children.Add(Row("Title screen", screenshotRow));

            if (!icon)
            {
                if (styles.Count > 1)
                    form.Children.Add(Row("Style", styleBox));
                form.Children.Add(Row("Name", line1));
                form.Children.Add(line2);
                var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                details.Children.Add(Row("Released", year));
                details.Children.Add(Row("Players", players));
                form.Children.Add(details);

                styleBox.SelectionChanged += (s, e) => Render();
                foreach (TextBox box in new[] { line1, line2, year, players })
                    box.TextChanged += (s, e) => Render();
            }

            var save = new Button { Content = "Use this image", MinWidth = 120, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            save.Classes.Add("primary");
            var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            save.Click += (s, e) => Save();
            cancel.Click += (s, e) => Close(null);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);

            var previewBorder = new Border
            {
                Child = preview,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Width = icon ? 256 : 512,
                Height = icon ? 256 : 288,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var body = icon ? (Panel)new StackPanel { Spacing = 16 } : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            body.Children.Add(form);
            body.Children.Add(previewBorder);

            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(body);
            root.Children.Add(buttons);
            Content = root;

            Closed += (s, e) =>
            {
                screenshot?.Dispose();
                current?.Dispose();
            };
            Render();
        }

        private static int Number(TextBox box) => int.TryParse(box.Text, out int value) && value > 0 ? value : 0;

        private void Render()
        {
            try
            {
                SKBitmap image = icon
                    ? BootImageComposer.CreateIcon(console, screenshot)
                    : BootImageComposer.CreateTv(styles[Math.Max(0, styleBox.SelectedIndex)], screenshot, line1.Text, line2.Text, Number(year), Number(players));
                current?.Dispose();
                current = image;

                using SKImage encoded = SKImage.FromBitmap(image);
                using SKData data = encoded.Encode(SKEncodedImageFormat.Png, 90);
                using var stream = new MemoryStream(data.ToArray());
                preview.Source = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Logger.Log("Image creator: " + ex);
            }
        }

        private void Save()
        {
            if (current == null)
            {
                Close(null);
                return;
            }
            // A fresh name each time, so an earlier image that is still selected elsewhere is not overwritten.
            string name = (icon ? "iconTex" : "bootTvTex") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Close(BootImageComposer.Save(current, name));
        }
    }
}
