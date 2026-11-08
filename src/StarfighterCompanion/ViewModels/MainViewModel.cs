using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Model;
using StarfighterCompanion.Services;
using StarfighterCompanion.Views;

namespace StarfighterCompanion.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private const int MaxSearchResults = 3000;

    private readonly Window _window;
    private PilotDocument? _document;
    private string? _path;
    private GroupNode? _selectedGroup;
    private string _searchText = string.Empty;
    private string _status = "Open a pilot file to begin (File › Open, or drop it on this window).";
    private int _errorCount;
    private int _warningCount;
    private IssueViewModel? _selectedIssue;

    public MainViewModel(Window window)
    {
        _window = window;
        OpenCommand = new Command(OpenAsync);
        SaveCommand = new Command(SaveAsync, () => _document is not null);
        SaveAsCommand = new Command(SaveAsAsync, () => _document is not null);
        RevertCommand = new Command(RevertAsync, () => _document is not null && _path is not null);
        AboutCommand = new Command(AboutAsync);
        ExitCommand = new Command(() => _window.Close());
        InitializeLibrary();
    }

    public Command OpenCommand { get; }
    public Command SaveCommand { get; }
    public Command SaveAsCommand { get; }
    public Command RevertCommand { get; }
    public Command AboutCommand { get; }
    public Command ExitCommand { get; }

    public bool HasDocument => _document is not null;

    public bool HasNoDocument => _document is null;

    public string WindowTitle => _document is null
        ? "Starfighter Companion"
        : $"{(_document.IsDirty ? "• " : string.Empty)}{_document.PilotName} - {Path.GetFileName(_path)} - Starfighter Companion";

    public string PilotTitle => _document?.PilotName ?? string.Empty;

    public string FormatTitle => _document is null ? string.Empty : $"{_document.GameName} · {_document.FormatName}";

    public string FilePath => _path ?? string.Empty;

    public ObservableCollection<string> Notes { get; } = new();

    public ObservableCollection<GroupNode> Groups { get; } = new();

    public ObservableCollection<FieldViewModel> VisibleFields { get; } = new();

    public ObservableCollection<IssueViewModel> Issues { get; } = new();

    public GroupNode? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (Set(ref _selectedGroup, value))
            {
                if (value is not null && _searchText.Length > 0)
                {
                    _searchText = string.Empty;
                    Raise(nameof(SearchText));
                }

                ShowFields();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                ShowFields();
            }
        }
    }

    public string ListHeader => _searchText.Length > 0
        ? $"Search results for \"{_searchText}\""
        : _selectedGroup?.Path.Replace("/", " › ") ?? string.Empty;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => Set(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => Set(ref _warningCount, value);
    }

    public string IssueSummary => _document is null
        ? string.Empty
        : ErrorCount == 0 && WarningCount == 0
            ? "✔ No problems found"
            : $"{ErrorCount} error(s) · {WarningCount} warning(s)";

    public IssueViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (Set(ref _selectedIssue, value) && value?.Issue.Field is { } editableField)
            {
                GoTo(editableField);
            }
        }
    }

    public async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var patterns = PilotFormats.AllExtensions.Select(e => "*" + e).ToList();
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open pilot file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Pilot files") { Patterns = patterns },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            await OpenPathAsync(path, confirm: false);
        }
    }

    public async Task OpenPathAsync(string path, bool confirm = true)
    {
        if (confirm && !await ConfirmDiscardAsync())
        {
            return;
        }

        try
        {
            var document = PilotFormats.Open(path);
            Load(document, path);
            ShowSettings = false;
            Status = $"Opened {Path.GetFileName(path)}.";
        }
        catch (UnsupportedPilotFileException ex)
        {
            await MessageDialog.ShowAsync(_window, "Not a supported pilot file", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await MessageDialog.ShowAsync(_window, "Could not open the file", ex.Message);
        }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (_document is null || !_document.IsDirty)
        {
            return true;
        }

        var answer = await MessageDialog.AskAsync(_window, "Unsaved changes",
            $"{_document.PilotName} has unsaved changes. Save them first?", "Save", "Discard", "Cancel");
        return answer switch
        {
            0 => await SaveAsync(),
            1 => true,
            _ => false,
        };
    }

    private async Task<bool> SaveAsync()
    {
        if (_document is null)
        {
            return false;
        }

        if (_path is null)
        {
            return await SaveAsAsync();
        }

        return await WriteAsync(_path, makeBackups: true);
    }

    private async Task<bool> SaveAsAsync()
    {
        if (_document is null)
        {
            return false;
        }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save pilot file as",
            SuggestedFileName = _path is null ? null : Path.GetFileName(_path),
            DefaultExtension = _path is null ? null : Path.GetExtension(_path),
            ShowOverwritePrompt = true,
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return false;
        }

        if (!await WriteAsync(path, makeBackups: File.Exists(path)))
        {
            return false;
        }

        _path = path;
        RaiseDocumentTitles();
        return true;
    }

    private async Task<bool> WriteAsync(string path, bool makeBackups)
    {
        if (_document is null)
        {
            return false;
        }

        if (!_document.CanSave(out var issues))
        {
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => "• " + i.Message);
            await MessageDialog.ShowAsync(_window, "Cannot save yet",
                "These problems would break the pilot in the game. Fix them first:\n\n" + string.Join("\n", errors));
            Revalidate();
            return false;
        }

        try
        {
            var bytes = _document.Save();
            var backups = makeBackups ? PilotFileService.Save(path, bytes) : SaveWithoutBackup(path, bytes);
            Status = backups.Count == 0
                ? $"Saved {Path.GetFileName(path)}."
                : $"Saved {Path.GetFileName(path)}. Backup: {string.Join(", ", backups.Select(Path.GetFileName))}.";
            RaiseDocumentTitles();
            foreach (var row in VisibleFields)
            {
                row.Refresh();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(_window, "Could not save the file",
                ex.Message + "\n\nIf the game is running, close it and try again.");
            return false;
        }
    }

    private static IReadOnlyList<string> SaveWithoutBackup(string path, byte[] bytes)
    {
        PilotFileService.Save(path, bytes);
        return Array.Empty<string>();
    }

    private async Task RevertAsync()
    {
        if (_path is null || _document is null)
        {
            return;
        }

        if (_document.IsDirty &&
            await MessageDialog.AskAsync(_window, "Revert", "Throw away all changes and reload the file from disk?", "Revert", "Cancel") != 0)
        {
            return;
        }

        await OpenPathAsync(_path, confirm: false);
    }

    private Task AboutAsync() => MessageDialog.ShowAsync(_window, "About Starfighter Companion",
        "Starfighter Companion " + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) + "\n\n" +
        "A pilot file editor for the X-Wing series: X-Wing, TIE Fighter, X-Wing vs. TIE Fighter, Balance of Power, " +
        "X-Wing Alliance (including XWAUP and TFTC), the OpenXW family of reimplementations, and XWVM.\n\n" +
        "Values are checked as you type, and a pilot that would break the game cannot be saved. " +
        "Before a file is overwritten, the original is kept as .original.bak and the previous version as .bak.\n\n" +
        "File layouts are based on the OpenXW, OpenTIE, OpenXvT and OpenXWA projects (GPL-3.0) and on XWVM.\n\n" +
        "Free software under the GNU Affero General Public License v3.\n" +
        "Star Wars and related names are trademarks of Lucasfilm Ltd. This project is not affiliated with Lucasfilm, Disney or LucasArts.");

    private void Load(PilotDocument document, string path)
    {
        if (_document is not null)
        {
            _document.FieldChanged -= OnFieldChanged;
        }

        _document = document;
        _path = path;
        _document.FieldChanged += OnFieldChanged;

        Notes.Clear();
        foreach (var note in document.Notes)
        {
            Notes.Add(note);
        }

        Groups.Clear();
        foreach (var group in GroupNode.Build(document.Fields))
        {
            Groups.Add(group);
        }

        _searchText = string.Empty;
        Raise(nameof(SearchText));
        _selectedGroup = null;
        SelectedGroup = Groups.FirstOrDefault();

        Revalidate();
        RaiseDocumentTitles();
        Raise(nameof(SelectedPilotItem));
        Raise(nameof(HasDocument));
        Raise(nameof(HasNoDocument));
        RaiseContentState();
        SaveCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }

    private void OnFieldChanged(object? sender, EditableField field)
    {
        // Dependent values (e.g. a rating name) may have changed too.
        foreach (var row in VisibleFields)
        {
            if (!ReferenceEquals(row.Field, field))
            {
                row.Refresh();
            }
        }

        Revalidate();
        RaiseDocumentTitles();
    }

    private void ShowFields()
    {
        VisibleFields.Clear();
        if (_document is not null)
        {
            if (_searchText.Length > 0)
            {
                var terms = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var field in _document.Fields
                             .Where(f => terms.All(t => f.Label.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                        f.Group.Contains(t, StringComparison.OrdinalIgnoreCase)))
                             .Take(MaxSearchResults))
                {
                    VisibleFields.Add(new FieldViewModel(field, showGroup: true));
                }
            }
            else if (_selectedGroup is not null)
            {
                foreach (var field in _selectedGroup.Fields)
                {
                    VisibleFields.Add(new FieldViewModel(field, showGroup: false));
                }
            }
        }

        Raise(nameof(ListHeader));
        Raise(nameof(EmptyListHint));
    }

    public string EmptyListHint => VisibleFields.Count > 0 || _document is null
        ? string.Empty
        : _searchText.Length > 0 ? "Nothing matches the search." : "Pick a sub-section on the left.";

    private void GoTo(EditableField field)
    {
        var node = Groups.SelectMany(g => g.SelfAndDescendants()).FirstOrDefault(n => n.Fields.Contains(field));
        if (node is not null)
        {
            SelectedGroup = node;
        }
    }

    private void Revalidate()
    {
        Issues.Clear();
        if (_document is null)
        {
            return;
        }

        var issues = _document.Validate();
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
        {
            Issues.Add(new IssueViewModel(issue));
        }

        ErrorCount = issues.Count(i => i.Severity == IssueSeverity.Error);
        WarningCount = issues.Count(i => i.Severity == IssueSeverity.Warning);
        Raise(nameof(IssueSummary));
    }

    private void RaiseDocumentTitles()
    {
        Raise(nameof(WindowTitle));
        Raise(nameof(PilotTitle));
        Raise(nameof(FormatTitle));
        Raise(nameof(FilePath));
    }
}
