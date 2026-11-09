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

    public const string XwaUpgradePage = "https://www.xwaupgrade.com/";

    /// <summary>The OpenXW family's author.</summary>
    public const string OpenFamilyAuthor = "https://github.com/elyosh";

    public static readonly (string Name, string Url)[] OpenFamily =
    {
        ("OpenXW", "https://github.com/elyosh/OpenXW"),
        ("OpenTIE", "https://github.com/elyosh/OpenTIE"),
        ("OpenXvT", "https://github.com/elyosh/OpenXvT"),
        ("OpenXWA", "https://github.com/elyosh/OpenXWA"),
    };

    /// <summary>Where to buy the games: (game, GOG page, Steam page).</summary>
    public static readonly (string Game, string Gog, string Steam)[] Stores =
    {
        ("X-Wing Special Edition", "https://www.gog.com/en/game/star_wars_xwing_special_edition", "https://store.steampowered.com/app/354430/"),
        ("TIE Fighter Special Edition", "https://www.gog.com/en/game/star_wars_tie_fighter_special_edition", "https://store.steampowered.com/app/355250/"),
        ("X-Wing vs. TIE Fighter + Balance of Power", "https://www.gog.com/en/game/star_wars_xwing_vs_tie_fighter", "https://store.steampowered.com/app/361690/"),
        ("X-Wing Alliance", "https://www.gog.com/en/game/star_wars_xwing_alliance", "https://store.steampowered.com/app/361670/"),
    };

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

        static Control Links(params (string Label, string Url)[] links)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (label, url) in links)
            {
                var link = Link(label, url);
                link.Margin = new Thickness(0, 0, 18, 0);
                row.Children.Add(link);
            }

            return row;
        }

        // A collapsible entry: closed at first, the title opens it.
        static Control Section(string title, params Control[] body)
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.AddRange(body);
            return new Expander
            {
                Header = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold },
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
        }

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
                     "X-Wing Alliance (including XWAUP, TFTC and Embers of the Empire), the OpenXW family of reimplementations, and XWVM."),
                Text("Values are checked as you type, and a pilot that would break the game cannot be saved. Before a file is " +
                     "overwritten, the original is kept as .original.bak and the previous version as .bak."),
                new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            Section("XWVM",
                                Text("X-Wing Virtual Machine is a fan-made remake of X-Wing (and soon TIE Fighter) in a modern engine, " +
                                     "using the data files of your own copy of the game.", 12, 0.85),
                                Text("Starfighter Companion edits XWVM's .vmpilot files and can move X-Wing and TIE Fighter pilots " +
                                     "into and out of XWVM (Convert / export…).", 12, 0.85),
                                Links(("XWVM on ModDB", XwvmModPage), ("XWVM Discord", XwvmDiscord))),
                            Section("X-Wing Alliance Upgrade (XWAUP)",
                                Text("A long-running community project that replaces X-Wing Alliance's craft, cockpits and effects with " +
                                     "high-detail versions, and adds new craft and missions. It is installed into its own copy of XWA.", 12, 0.85),
                                Text("Its pilots use the X-Wing Alliance pilot file, so Starfighter Companion edits them as the " +
                                     "\"X-Wing Alliance Upgrade\" version. Craft numbers it adds are shown by number.", 12, 0.85),
                                Links(("X-Wing Alliance Upgrade website", XwaUpgradePage))),
                            Section("OpenXW, OpenTIE, OpenXvT and OpenXWA",
                                Text("Open-source reimplementations of the original games' engines (GPL-3.0). They run the original " +
                                     "game data on current Windows, macOS and Linux, and read and write the original pilot files.", 12, 0.85),
                                Text("Their source code is where Starfighter Companion's pilot file layouts, and the DOS X-Wing " +
                                     "portrait rule, come from.", 12, 0.85),
                                Links(OpenFamily.Append(("All projects by their author", OpenFamilyAuthor)).ToArray())),
                            Section("Get the games",
                                new Control[] { Text("You need your own copy of the games. They are sold on GOG (DRM-free) and Steam:", 12, 0.85) }
                                    .Concat(Stores.Select(store => (Control)new DockPanel
                                    {
                                        Margin = new Thickness(0, 2, 0, 0),
                                        Children =
                                        {
                                            new StackPanel
                                            {
                                                [DockPanel.DockProperty] = Dock.Right,
                                                Orientation = Orientation.Horizontal,
                                                Spacing = 14,
                                                Children = { Link("GOG", store.Gog), Link("Steam", store.Steam) },
                                            },
                                            new TextBlock { Text = store.Game, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                                        },
                                    }))
                                    .ToArray()),
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
