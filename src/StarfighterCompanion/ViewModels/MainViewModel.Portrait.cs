using Avalonia.Media.Imaging;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Services;
using StarfighterCompanion.Views;

namespace StarfighterCompanion.ViewModels;

/// <summary>
/// X-Wing pilot portrait and renaming. The DOS game chooses a pilot's face from the pilot's name, and the name is the
/// file name; typing a new name shows the face the game will give the pilot, and Rename renames the file.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Folders searched for the DOS PILOT.LFD, the editions that have it first.</summary>
    private static readonly string[] PortraitVariants = { "xwing94", "xwing93", "xwing98", "openxw" };

    /// <summary>The DOS screen has 320×200 pixels shown on a 4:3 monitor, so each pixel is 1.2 times taller than wide.</summary>
    private const double DosPixelAspect = 1.2;

    private const double PortraitScale = 3;

    private GameArtService.PortraitSet? _portraits;
    private bool _portraitsSearched;
    private string _pilotNameDraft = string.Empty;
    private string? _renameError;

    public Command RenameCommand { get; private set; } = null!;

    /// <summary>X-Wing pilots get a portrait and a name box.</summary>
    public bool IsXWingPilot => _document is not null && _format is XWingPilotFormat;

    public Bitmap? Portrait => _portraits?.For(_pilotNameDraft);

    public bool HasPortrait => Portrait is not null;

    public bool HasNoPortrait => !HasPortrait;

    public double PortraitWidth => (_portraits?.Width ?? 38) * PortraitScale;

    public double PortraitHeight => (_portraits?.Height ?? 48) * PortraitScale * DosPixelAspect;

    public string PortraitCaption => _portraits is not null
        ? $"Face {XWingPortraits.FaceIndex(_pilotNameDraft) + 1} of {XWingPortraits.FaceCount}"
        : _portraitsSearched ? "No portrait" : "Looking…";

    public string PortraitHelp => _portraits is not null
        ? $"The face DOS X-Wing shows for this name on its pilot selection screen. Read from {_portraits.Source}."
        : "The portraits are read from the DOS edition of X-Wing (RESOURCE\\PILOT.LFD). Install the 1993 or 1994 edition, " +
          "or set its folder in Settings, to see the face the game gives this pilot.";

    /// <summary>The name being typed. The portrait follows it as it changes; Rename applies it.</summary>
    public string PilotNameDraft
    {
        get => _pilotNameDraft;
        set
        {
            if (Set(ref _pilotNameDraft, value ?? string.Empty))
            {
                RenameError = CheckNewName(_pilotNameDraft);
                RaisePortrait();
                RenameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? RenameError
    {
        get => _renameError;
        private set
        {
            if (Set(ref _renameError, value))
            {
                Raise(nameof(HasRenameError));
            }
        }
    }

    public bool HasRenameError => !string.IsNullOrEmpty(_renameError);

    private void InitializePortrait()
    {
        RenameCommand = new Command(RenameAsync, () => IsXWingPilot && _path is not null && CanRenameTo(_pilotNameDraft));
    }

    /// <summary>The bitmap for a pilot name, for the convert dialog; null when the portraits are not available.</summary>
    private Bitmap? PortraitFor(string pilotName) => _portraits?.For(pilotName);

    private async Task ReloadPortraitAsync()
    {
        var folders = PortraitVariants.Select(InstalledFolder).OfType<string>().ToList();
        _portraits = await _art.PortraitsAsync(folders);
        _portraitsSearched = true;
        RaisePortrait();
    }

    private void OnDocumentLoadedForPortrait()
    {
        _pilotNameDraft = _document?.PilotName ?? string.Empty;
        _renameError = null;
        Raise(nameof(PilotNameDraft));
        Raise(nameof(RenameError));
        Raise(nameof(HasRenameError));
        Raise(nameof(IsXWingPilot));
        RaisePortrait();
        RenameCommand.RaiseCanExecuteChanged();
        if (IsXWingPilot && !_portraitsSearched)
        {
            _ = ReloadPortraitAsync();
        }
    }

    private void RaisePortrait()
    {
        Raise(nameof(Portrait));
        Raise(nameof(HasPortrait));
        Raise(nameof(HasNoPortrait));
        Raise(nameof(PortraitWidth));
        Raise(nameof(PortraitHeight));
        Raise(nameof(PortraitCaption));
        Raise(nameof(PortraitHelp));
    }

    private Edition? CurrentEdition => _document?.Edition ?? (_format is null ? null : Edition.For(null, _format));

    private string? CheckNewName(string name)
    {
        var edition = CurrentEdition;
        if (!IsXWingPilot || edition is null || IsCurrentName(name))
        {
            return null;
        }

        return edition.CheckPilotName(name) ?? (TargetExists(name) ? $"A pilot named {name.ToUpperInvariant()} already exists in this folder." : null);
    }

    private bool CanRenameTo(string name) => !IsCurrentName(name) && CheckNewName(name) is null;

    private bool IsCurrentName(string name) =>
        _document is not null && string.Equals(name.Trim(), _document.PilotName, StringComparison.Ordinal);

    private string? RenamedPath(string name) =>
        _path is null ? null : Path.Combine(Path.GetDirectoryName(_path) ?? string.Empty, name.ToUpperInvariant() + Path.GetExtension(_path));

    private bool TargetExists(string name)
    {
        var target = RenamedPath(name);
        return target is not null && !string.Equals(target, _path, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(Path.GetDirectoryName(target)) &&
               Directory.EnumerateFiles(Path.GetDirectoryName(target)!, Path.GetFileName(target),
                   new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
    }

    /// <summary>
    /// Renames the pilot. X-Wing has no name inside the pilot file: the registration desk lists the .PLT files and
    /// uses their names (in upper case), so the file is renamed. Backups keep their old names.
    /// </summary>
    private async Task RenameAsync()
    {
        if (_document is null || _path is null || !CanRenameTo(_pilotNameDraft))
        {
            return;
        }

        if (_document.IsDirty)
        {
            var answer = await MessageDialog.AskAsync(_window, "Save first?",
                "The pilot has unsaved changes. They are saved before the pilot is renamed.", "Save and rename", "Cancel");
            if (answer != 0 || !await SaveAsync())
            {
                return;
            }
        }

        var oldPath = _path;
        var newName = _pilotNameDraft.Trim().ToUpperInvariant();
        var newPath = RenamedPath(newName)!;
        try
        {
            File.Move(oldPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(_window, "Could not rename the pilot",
                ex.Message + "\n\nIf the game is running, close it and try again.");
            return;
        }

        var variantId = _currentVariant?.Variant.Id ?? _document.Edition?.Id;
        await OpenPathAsync(newPath, confirm: false, variantId);
        if (_currentVariant is not null)
        {
            await ScanPilotsAsync(_currentVariant);
        }

        Status = $"Renamed {Path.GetFileName(oldPath)} to {Path.GetFileName(newPath)}.";
    }
}
