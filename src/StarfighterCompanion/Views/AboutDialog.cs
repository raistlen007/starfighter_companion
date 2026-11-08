using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StarfighterCompanion.Views;

/// <summary>Help › About.</summary>
public static class AboutDialog
{
    public const string XwvmModPage = "https://www.moddb.com/mods/xwvm";

    // The invite XWVM's own README points to ("Join our Discord" on the ModDB page).
    public const string XwvmDiscord = "https://discord.gg/nfQN9ht";

    public const string ProjectPage = "https://github.com/raistlen007/starfighter_companion";

    public static async Task ShowAsync(Window owner)
    {
        var version = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? string.Empty;
        var dialog = new Window
        {
            Title = "About Starfighter Companion",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        static TextBlock Text(string text, double size = 13, double opacity = 1, FontWeight weight = FontWeight.Normal) => new()
        {
            Text = text,
            FontSize = size,
            Opacity = opacity,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
        };

        static Control Link(string label, string url) => new HyperlinkButton
        {
            Content = label,
            NavigateUri = new Uri(url),
            Padding = new Thickness(0, 2),
            [ToolTip.TipProperty] = url,
        };

        Image? logo = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StarfighterCompanion/Assets/icon-128.png"));
            logo = new Image { Source = new Bitmap(stream), Width = 88, Height = 88, VerticalAlignment = VerticalAlignment.Top };
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            // No logo is not worth failing over.
        }

        var close = new Button { Content = "Close", MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (logo is not null)
        {
            DockPanel.SetDock(logo, Dock.Left);
            logo.Margin = new Thickness(0, 0, 18, 0);
            header.Children.Add(logo);
        }

        header.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                Text("Starfighter Companion", 24, weight: FontWeight.SemiBold),
                Text($"Version {version}", 12, 0.7),
                Text("Made by Rasi", 14, weight: FontWeight.SemiBold),
                Link("Project page and source code", ProjectPage),
            },
        });

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 10,
            Children =
            {
                header,
                Text("A pilot file editor for the X-Wing series: X-Wing, TIE Fighter, X-Wing vs. TIE Fighter, Balance of Power, " +
                     "X-Wing Alliance (including XWAUP and TFTC), the OpenXW family of reimplementations, and XWVM."),
                Text("Values are checked as you type, and a pilot that would break the game cannot be saved. Before a file is " +
                     "overwritten, the original is kept as .original.bak and the previous version as .bak."),
                new Border
                {
                    Padding = new Thickness(12, 10),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            Text("XWVM", 14, weight: FontWeight.SemiBold),
                            Text("X-Wing Virtual Machine plays the original X-Wing (and soon TIE Fighter) on modern systems. " +
                                 "Starfighter Companion can move pilots into and out of it.", 12, 0.85),
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 18,
                                Margin = new Thickness(0, 4, 0, 0),
                                Children = { Link("XWVM on ModDB", XwvmModPage), Link("XWVM Discord", XwvmDiscord) },
                            },
                        },
                    },
                },
                Text("File layouts are based on the OpenXW, OpenTIE, OpenXvT and OpenXWA projects (GPL-3.0) and on XWVM.", 12, 0.8),
                Text("Free software under the GNU Affero General Public License v3. Star Wars and related names are trademarks of " +
                     "Lucasfilm Ltd. This project is not affiliated with Lucasfilm, Disney or LucasArts.", 12, 0.8),
                close,
            },
        };

        await dialog.ShowDialog(owner);
    }
}
