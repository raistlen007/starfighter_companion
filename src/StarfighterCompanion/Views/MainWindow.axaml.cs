using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StarfighterCompanion.ViewModels;

namespace StarfighterCompanion.Views;

public partial class MainWindow : Window
{
    private bool _closingConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// The game and version menus act on a click and never keep a selection: a still-selected entry could not be
    /// clicked again after coming back to the menu, because clicking it would not change the selection.
    /// </summary>
    private void OnMenuSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not { } item || ViewModel is not { } vm)
        {
            return;
        }

        list.SelectedItem = null;
        switch (item)
        {
            case GameItemViewModel game:
                vm.SelectedGameItem = game;
                break;
            case VariantItemViewModel variant:
                vm.SelectedVariantItem = variant;
                break;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Enter commits a number or text being typed (the value is applied when the box loses focus).
        if (e.Key == Key.Enter && e.Source is TextBox box && !ReferenceEquals(box, SearchBox))
        {
            FieldList.Focus();
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null && ViewModel is { } vm)
        {
            await vm.OpenPathAsync(path);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closingConfirmed || ViewModel is null)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (await ViewModel.ConfirmDiscardAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }
}
