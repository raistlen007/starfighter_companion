using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarfighterCompanion.Views;

/// <summary>Small modal message box built in code.</summary>
public static class MessageDialog
{
    public static Task ShowAsync(Window owner, string title, string message) =>
        AskAsync(owner, title, message, "OK");

    /// <summary>Shows the message with the given buttons and returns the index of the button pressed (-1 if closed).</summary>
    public static async Task<int> AskAsync(Window owner, string title, string message, params string[] buttons)
    {
        var result = -1;
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 90, IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            button.Click += (_, _) =>
            {
                result = index;
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new Border { [DockPanel.DockProperty] = Dock.Bottom, Margin = new Thickness(0, 16, 0, 0), Child = buttonRow },
                new ScrollViewer
                {
                    MaxHeight = 520,
                    Content = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
