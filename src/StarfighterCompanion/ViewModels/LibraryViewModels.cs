using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using StarfighterCompanion.Core.Library;

namespace StarfighterCompanion.ViewModels;

public enum NavigationPage
{
    Games,
    Variants,
    Pilots,
}

public sealed class GameItemViewModel : ViewModelBase
{
    private string _summary = string.Empty;
    private bool _isAvailable;

    public GameItemViewModel(GameDefinition game) => Game = game;

    public GameDefinition Game { get; }

    public string Name => Game.Name;

    public string Summary
    {
        get => _summary;
        set => Set(ref _summary, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (Set(ref _isAvailable, value))
            {
                Raise(nameof(Opacity));
            }
        }
    }

    public double Opacity => _isAvailable ? 1.0 : 0.55;

    /// <summary>The icon of the first installed version that has one; null shows the placeholder.</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    private Bitmap? _icon;
}

public sealed class VariantItemViewModel : ViewModelBase
{
    private Bitmap? _icon;

    public VariantItemViewModel(VariantDefinition variant, string? folder)
    {
        Variant = variant;
        Folder = folder;
    }

    public VariantDefinition Variant { get; }

    public string? Folder { get; }

    public string Name => Variant.Name;

    public bool IsAvailable => Folder is not null && Directory.Exists(Folder);

    public string Summary => IsAvailable ? Folder! : "Not found. Set its folder in Settings.";

    public double Opacity => IsAvailable ? 1.0 : 0.55;

    /// <summary>The icon read from this version's folder; null shows the placeholder.</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }
}

public sealed class PilotItemViewModel
{
    public PilotItemViewModel(PilotEntry entry) => Entry = entry;

    public PilotEntry Entry { get; }

    public string Name => Entry.Name;

    public string Detail => $"{Entry.FileName} · {Entry.LastWrite:d MMM yyyy, HH:mm}";
}

/// <summary>One folder setting row.</summary>
public sealed class VariantSettingViewModel : ViewModelBase
{
    private readonly GameLibrary _library;
    private readonly Action _changed;
    private readonly Func<VariantSettingViewModel, Task> _browse;
    private string _folderText = string.Empty;
    private int _pilotCount = -1;
    private Bitmap? _icon;

    public VariantSettingViewModel(VariantDefinition variant, GameLibrary library, Action changed, Func<VariantSettingViewModel, Task> browse)
    {
        Variant = variant;
        _library = library;
        _changed = changed;
        _browse = browse;
        BrowseCommand = new Command(() => _browse(this));
        AutoCommand = new Command(UseAuto);
        ClearCommand = new Command(Clear);
        Refresh();
    }

    public VariantDefinition Variant { get; }

    public string Name => Variant.Name;

    public string? Description => Variant.Description;

    public bool HasDescription => !string.IsNullOrEmpty(Variant.Description);

    public Command BrowseCommand { get; }

    public Command AutoCommand { get; }

    public Command ClearCommand { get; }

    /// <summary>The folder in effect. Typing a path and leaving the box stores it as the user's choice.</summary>
    public string FolderText
    {
        get => _folderText;
        set
        {
            value = value?.Trim() ?? string.Empty;
            if (value == (_library.FolderFor(Variant.Id) ?? string.Empty))
            {
                return;
            }

            _library.Settings.SetFolder(Variant.Id, value);
            Commit();
        }
    }

    public string Status
    {
        get
        {
            var folder = _library.FolderFor(Variant.Id);
            var origin = _library.Settings.IsUserSet(Variant.Id)
                ? (folder is null ? "Turned off" : "Set by you")
                : _library.Detected.TryGetValue(Variant.Id, out var d) ? $"Found automatically ({d.Source})" : "Not found";

            if (folder is null)
            {
                return origin;
            }

            if (!Directory.Exists(folder))
            {
                return origin + " · folder does not exist";
            }

            return _pilotCount < 0 ? origin : $"{origin} · {_pilotCount} pilot{(_pilotCount == 1 ? string.Empty : "s")}";
        }
    }

    public bool CanUseAuto => _library.Settings.IsUserSet(Variant.Id);

    /// <summary>The icon read from the folder in effect; null shows the placeholder.</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public void SetFolder(string folder)
    {
        _library.Settings.SetFolder(Variant.Id, folder);
        Commit();
    }

    public async Task CountPilotsAsync()
    {
        var folder = _library.FolderFor(Variant.Id);
        var variant = Variant;
        _pilotCount = folder is null ? -1 : await Task.Run(() => PilotScanner.Scan(variant, folder).Count);
        Raise(nameof(Status));
    }

    public void Refresh()
    {
        _folderText = _library.FolderFor(Variant.Id) ?? string.Empty;
        Raise(nameof(FolderText));
        Raise(nameof(Status));
        Raise(nameof(CanUseAuto));
    }

    private void UseAuto()
    {
        _library.Settings.UseAutoDetection(Variant.Id);
        Commit();
    }

    private void Clear()
    {
        _library.Settings.SetFolder(Variant.Id, string.Empty);
        Commit();
    }

    private void Commit()
    {
        _pilotCount = -1;
        Refresh();
        _changed();
        _ = CountPilotsAsync();
    }
}

public sealed class SettingsGameViewModel
{
    public SettingsGameViewModel(string name, IEnumerable<VariantSettingViewModel> variants)
    {
        Name = name;
        Variants = new ObservableCollection<VariantSettingViewModel>(variants);
    }

    public string Name { get; }

    public ObservableCollection<VariantSettingViewModel> Variants { get; }
}
