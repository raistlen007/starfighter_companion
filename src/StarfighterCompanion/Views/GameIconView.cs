using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarfighterCompanion.Views;

/// <summary>
/// A game's icon, taken from its installation. When there is none (the game is not installed, or the edition has no
/// icon of its own) a generic red question mark is shown instead; no game artwork ships with the program.
/// </summary>
public sealed class GameIconView : Panel
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<GameIconView, IImage?>(nameof(Source));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _placeholder;

    public GameIconView()
    {
        RenderOptions.SetBitmapInterpolationMode(_image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        _placeholder = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.Parse("#D32F2F")),
            Background = new SolidColorBrush(Color.Parse("#26D32F2F")),
            Child = new TextBlock
            {
                Text = "?",
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#F44336")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Children.Add(_placeholder);
        Children.Add(_image);
        Width = Height = 32;
        Update();
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == HeightProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        if (_placeholder is null)
        {
            return; // a property set while the base class is still being constructed
        }

        _image.Source = Source;
        _image.IsVisible = Source is not null;
        _placeholder.IsVisible = Source is null;
        if (_placeholder.Child is TextBlock text && !double.IsNaN(Height))
        {
            text.FontSize = Math.Max(10, Height * 0.62);
        }

        ToolTip.SetTip(this, Source is null ? "No icon: the game was not found, or this version has no icon of its own." : null);
    }
}
