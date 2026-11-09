using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Services;

namespace StarfighterCompanion.ViewModels;

/// <summary>Game menu (left side) and Settings.</summary>
public sealed partial class MainViewModel
{
    private readonly GameArtService _art = new();
    private GameLibrary _library = null!;
    private NavigationPage _page = NavigationPage.Games;
    private GameItemViewModel? _currentGame;
    private VariantItemViewModel? _currentVariant;
    private bool _showSettings;
    private bool _isScanning;
    private bool _isDetecting;
    private string _pilotListHint = string.Empty;

    public ObservableCollection<GameItemViewModel> GameItems { get; } = new();

    public ObservableCollection<VariantItemViewModel> VariantItems { get; } = new();

    public ObservableCollection<PilotItemViewModel> PilotItems { get; } = new();

    public ObservableCollection<SettingsGameViewModel> SettingsGames { get; } = new();

    public Command BackToGamesCommand { get; private set; } = null!;
    public Command BackToVariantsCommand { get; private set; } = null!;
    public Command RefreshPilotsCommand { get; private set; } = null!;
    public Command ShowSettingsCommand { get; private set; } = null!;
    public Command CloseSettingsCommand { get; private set; } = null!;
    public Command DetectCommand { get; private set; } = null!;

    /// <summary>Which sidebar page is shown (0 games, 1 versions, 2 pilots); the sidebar slides between them.</summary>
    public int PageIndex => (int)_page;

    public bool IsGamesPage => _page == NavigationPage.Games;
    public bool IsVariantsPage => _page == NavigationPage.Variants;
    public bool IsPilotsPage => _page == NavigationPage.Pilots;

    public bool CanGoBackToVariants => _currentGame is not null && _currentGame.Game.Variants.Count > 1;

    public string CurrentGameName => _currentGame?.Name ?? string.Empty;

    public string CurrentVariantName => _currentVariant is null || !CanGoBackToVariants ? string.Empty : _currentVariant.Name;

    public string CurrentFolder => _currentVariant?.Folder ?? string.Empty;

    public bool ShowSettings
    {
        get => _showSettings;
        private set
        {
            if (Set(ref _showSettings, value))
            {
                RaiseContentState();
            }
        }
    }

    public bool ShowEditor => !_showSettings && HasDocument;

    public bool ShowWelcome => !_showSettings && !HasDocument;

    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set => Set(ref _isDetecting, value);
    }

    public string PilotListHint
    {
        get => _pilotListHint;
        private set
        {
            if (Set(ref _pilotListHint, value))
            {
                Raise(nameof(HasPilotListHint));
            }
        }
    }

    public bool HasPilotListHint => !string.IsNullOrEmpty(_pilotListHint);

    public GameItemViewModel? SelectedGameItem
    {
        get => null;
        set
        {
            if (value is not null)
            {
                OpenGame(value);
            }
        }
    }

    public VariantItemViewModel? SelectedVariantItem
    {
        get => null;
        set
        {
            if (value is not null)
            {
                _ = OpenVariantAsync(value);
            }
        }
    }

    public PilotItemViewModel? SelectedPilotItem
    {
        get => PilotItems.FirstOrDefault(p => string.Equals(p.Entry.Path, _path, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null && !string.Equals(value.Entry.Path, _path, StringComparison.OrdinalIgnoreCase))
            {
                _ = OpenPilotAsync(value);
            }
        }
    }

    private void InitializeLibrary()
    {
        _library = new GameLibrary(LibrarySettings.Load());
        BackToGamesCommand = new Command(BackToGames);
        BackToVariantsCommand = new Command(BackToVariants);
        RefreshPilotsCommand = new Command(() => _currentVariant is null ? Task.CompletedTask : ScanPilotsAsync(_currentVariant));
        ShowSettingsCommand = new Command(OpenSettings);
        CloseSettingsCommand = new Command(CloseSettings);
        DetectCommand = new Command(DetectAsync);

        foreach (var game in GameCatalog.Games)
        {
            GameItems.Add(new GameItemViewModel(game));
        }

        foreach (var game in GameCatalog.Games)
        {
            SettingsGames.Add(new SettingsGameViewModel(game.Name,
                game.Variants.Select(v => new VariantSettingViewModel(v, _library, OnSettingsChanged, BrowseFolderAsync))));
        }

        RefreshGameSummaries();
        _ = DetectAsync();
    }

    private async Task DetectAsync()
    {
        IsDetecting = true;
        Status = "Looking for installed games…";
        try
        {
            var detected = await Task.Run(() => new InstallFinder().DetectAll());
            _library.ApplyDetection(detected);
            _art.Reset();
            foreach (var row in SettingsGames.SelectMany(g => g.Variants))
            {
                row.Refresh();
            }

            RefreshGameSummaries();
            await ReloadPortraitAsync();
            var games = GameItems.Count(g => g.IsAvailable);
            Status = games == 0
                ? "No installed games were found automatically. Set their folders in Settings."
                : $"Found {games} game{(games == 1 ? string.Empty : "s")}. Pick one on the left.";
            if (ShowSettings)
            {
                await CountAllPilotsAsync();
            }
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private void RefreshGameSummaries()
    {
        foreach (var item in GameItems)
        {
            var found = item.Game.Variants.Where(v => _library.FolderFor(v.Id) is { } f && Directory.Exists(f)).ToList();
            item.IsAvailable = found.Count > 0;
            item.Summary = found.Count switch
            {
                0 => "Not found",
                _ when item.Game.Variants.Count == 1 => "Installed",
                1 => found[0].Name,
                _ => $"{found.Count} versions",
            };
        }

        _ = RefreshIconsAsync();
    }

    /// <summary>Reads the icons of the games, versions and settings rows from the game folders (cached).</summary>
    private async Task RefreshIconsAsync()
    {
        foreach (var item in GameItems.ToList())
        {
            Avalonia.Media.Imaging.Bitmap? icon = null;
            foreach (var variant in item.Game.Variants)
            {
                icon = await _art.IconAsync(variant.Id, InstalledFolder(variant.Id));
                if (icon is not null)
                {
                    break;
                }
            }

            item.Icon = icon;
        }

        foreach (var item in VariantItems.ToList())
        {
            item.Icon = await _art.IconAsync(item.Variant.Id, item.IsAvailable ? item.Folder : null);
        }

        foreach (var row in SettingsGames.SelectMany(g => g.Variants).ToList())
        {
            row.Icon = await _art.IconAsync(row.Variant.Id, InstalledFolder(row.Variant.Id));
        }
    }

    private string? InstalledFolder(string variantId) =>
        _library.FolderFor(variantId) is { } folder && Directory.Exists(folder) ? folder : null;

    private void OpenGame(GameItemViewModel game)
    {
        _currentGame = game;
        VariantItems.Clear();
        foreach (var variant in game.Game.Variants)
        {
            VariantItems.Add(new VariantItemViewModel(variant, _library.FolderFor(variant.Id)));
        }

        _ = RefreshIconsAsync();
        if (game.Game.Variants.Count == 1)
        {
            _ = OpenVariantAsync(VariantItems[0]);
        }
        else
        {
            GoTo(NavigationPage.Variants);
        }

        Raise(nameof(CurrentGameName));
        Raise(nameof(CanGoBackToVariants));
    }

    private async Task OpenVariantAsync(VariantItemViewModel variant)
    {
        _currentVariant = variant;
        Raise(nameof(CurrentVariantName));
        Raise(nameof(CurrentFolder));
        GoTo(NavigationPage.Pilots);
        await ScanPilotsAsync(variant);
    }

    private async Task ScanPilotsAsync(VariantItemViewModel variant)
    {
        PilotItems.Clear();
        if (!variant.IsAvailable)
        {
            PilotListHint = "This version's folder is not set or does not exist. Open Settings to choose it.";
            return;
        }

        IsScanning = true;
        PilotListHint = "Looking for pilots…";
        try
        {
            var pilots = await Task.Run(() => PilotScanner.Scan(variant.Variant, variant.Folder));
            if (!ReferenceEquals(variant, _currentVariant))
            {
                return;
            }

            foreach (var pilot in pilots)
            {
                PilotItems.Add(new PilotItemViewModel(pilot));
            }

            PilotListHint = pilots.Count == 0 ? $"No pilots found in {variant.Folder}." : string.Empty;
            Raise(nameof(SelectedPilotItem));
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task OpenPilotAsync(PilotItemViewModel pilot)
    {
        await OpenPathAsync(pilot.Entry.Path, variantId: _currentVariant?.Variant.Id);
        Dispatcher.UIThread.Post(() => Raise(nameof(SelectedPilotItem)));
    }

    private void BackToGames()
    {
        _currentGame = null;
        _currentVariant = null;
        PilotItems.Clear();
        RefreshGameSummaries();
        GoTo(NavigationPage.Games);
    }

    private void BackToVariants()
    {
        if (_currentGame is null)
        {
            BackToGames();
            return;
        }

        _currentVariant = null;
        PilotItems.Clear();
        OpenGame(_currentGame);
    }

    private void GoTo(NavigationPage page)
    {
        _page = page;
        Raise(nameof(PageIndex));
        Raise(nameof(IsGamesPage));
        Raise(nameof(IsVariantsPage));
        Raise(nameof(IsPilotsPage));
    }

    private async Task OpenSettings()
    {
        ShowSettings = true;
        await CountAllPilotsAsync();
    }

    private void CloseSettings()
    {
        ShowSettings = false;
        RefreshAfterSettings();
    }

    private async Task CountAllPilotsAsync()
    {
        foreach (var row in SettingsGames.SelectMany(g => g.Variants))
        {
            await row.CountPilotsAsync();
        }
    }

    private void OnSettingsChanged()
    {
        try
        {
            _library.Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Could not save the settings: " + ex.Message;
        }

        _art.Reset();
        RefreshGameSummaries();
        _ = ReloadPortraitAsync();
    }

    private void RefreshAfterSettings()
    {
        RefreshGameSummaries();
        if (_currentGame is not null)
        {
            var variantId = _currentVariant?.Variant.Id;
            OpenGame(_currentGame);
            var again = VariantItems.FirstOrDefault(v => v.Variant.Id == variantId);
            if (again is not null && _currentGame.Game.Variants.Count > 1)
            {
                _ = OpenVariantAsync(again);
            }
        }
    }

    private async Task BrowseFolderAsync(VariantSettingViewModel row)
    {
        IStorageFolder? start = null;
        var current = _library.FolderFor(row.Variant.Id);
        if (current is not null && Directory.Exists(current))
        {
            start = await _window.StorageProvider.TryGetFolderFromPathAsync(current);
        }

        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Folder for {row.Name}",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            row.SetFolder(path);
        }
    }

    private async Task<string?> BrowseForVariantAsync(string variantId)
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Folder for {GameCatalog.Variant(variantId).Name}",
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private void RaiseContentState()
    {
        Raise(nameof(ShowEditor));
        Raise(nameof(ShowWelcome));
    }
}
