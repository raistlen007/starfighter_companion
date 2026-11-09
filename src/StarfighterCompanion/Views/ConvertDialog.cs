using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StarfighterCompanion.Core.Conversion;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Services;

namespace StarfighterCompanion.Views;

/// <summary>Converts the open pilot to another edition, or moves it into or out of XWVM.</summary>
public static class ConvertDialog
{
    /// <summary>What was written, so the caller can open it.</summary>
    public sealed record Outcome(string Path, ConversionTarget Target);

    public static async Task<Outcome?> ShowAsync(
        Window owner,
        byte[] data,
        PilotFormat format,
        Edition? source,
        string? sourceVariant,
        string pilotName,
        Func<string, string?> folderFor,
        Func<string, Task<string?>> browseFolder)
    {
        var targets = PilotConverter.TargetsFor(format, source, data, sourceVariant);
        Outcome? outcome = null;
        ConversionPlan? plan = null;
        string? folder = null;

        var dialog = new Window
        {
            Title = "Convert / export pilot",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var targetBox = new ComboBox { ItemsSource = targets, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var nameBox = new TextBox();
        var nameError = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var folderText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        var browse = new Button { Content = "Browse…" };
        var details = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        var convert = new Button { Content = "Convert", MinWidth = 90, IsDefault = true, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };

        ConversionTarget Target() => (ConversionTarget)targetBox.SelectedItem!;

        void Recompute()
        {
            plan = null;
            convert.IsEnabled = false;
            var target = Target();
            var error = PilotConverter.CheckName(target, nameBox.Text ?? string.Empty);
            nameError.Text = error ?? string.Empty;
            nameError.IsVisible = error is not null;
            folderText.Text = folder ?? "No folder is set for this version. Choose one with Browse, or set it in Settings.";

            if (error is not null || folder is null)
            {
                details.Text = string.Empty;
                return;
            }

            try
            {
                plan = PilotConverter.Plan(data, format, source, target, nameBox.Text!, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Xml.XmlException)
            {
                details.Text = ex.Message;
                return;
            }

            var lines = new List<string> { plan.Summary, string.Empty };
            lines.AddRange(plan.Result.Notes.Select(n => "• " + n));
            var errors = plan.Result.Warnings.Where(w => w.StartsWith("Error", StringComparison.Ordinal)).ToList();
            var warnings = plan.Result.Warnings.Except(errors).ToList();
            if (warnings.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Warnings:");
                lines.AddRange(warnings.Select(w => "⚠ " + w));
            }

            if (errors.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("This pilot cannot be converted to this edition:");
                lines.AddRange(errors.Select(e => "✖ " + e["Error: ".Length..]));
            }

            details.Text = string.Join("\n", lines);
            convert.IsEnabled = errors.Count == 0;
        }

        void TargetChanged()
        {
            var target = Target();
            folder = folderFor(target.VariantId);
            nameBox.Text = PilotConverter.SuggestName(format, pilotName, target, data);
            nameBox.MaxLength = target.Edition?.NameMaxLength ?? 64;
            Recompute();
        }

        targetBox.SelectionChanged += (_, _) => TargetChanged();
        nameBox.TextChanged += (_, _) => Recompute();
        browse.Click += async (_, _) =>
        {
            var chosen = await browseFolder(Target().VariantId);
            if (chosen is not null)
            {
                folder = chosen;
                Recompute();
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        convert.Click += async (_, _) =>
        {
            if (plan is null)
            {
                return;
            }

            if (plan.Overwrites &&
                await MessageDialog.AskAsync(dialog, "Replace pilot?", plan.Summary + "\n\nContinue?", "Replace", "Cancel") != 0)
            {
                return;
            }

            try
            {
                PilotFileService.Save(plan.Path, plan.Result.Data);
                outcome = new Outcome(plan.Path, Target());
                dialog.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await MessageDialog.ShowAsync(dialog, "Could not write the pilot",
                    ex.Message + "\n\nIf the game or XWVM is running, close it and try again.");
            }
        };

        Control Label(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 16, 0, 0),
                    Children = { convert, cancel },
                },
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "The open file is not changed; a new pilot file is written for the chosen version.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        Label("Convert to"),
                        targetBox,
                        Label("Pilot name"),
                        nameBox,
                        nameError,
                        Label("Folder"),
                        new DockPanel
                        {
                            Children =
                            {
                                new Border { [DockPanel.DockProperty] = Dock.Right, Margin = new Thickness(8, 0, 0, 0), Child = browse },
                                folderText,
                            },
                        },
                        new Border
                        {
                            Margin = new Thickness(0, 14, 0, 0),
                            Padding = new Thickness(10),
                            CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                            Child = new ScrollViewer { MaxHeight = 260, Content = details },
                        },
                    },
                },
            },
        };

        TargetChanged();
        await dialog.ShowDialog(owner);
        return outcome;
    }
}
