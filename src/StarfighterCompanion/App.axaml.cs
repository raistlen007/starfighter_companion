using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StarfighterCompanion.ViewModels;
using StarfighterCompanion.Views;

namespace StarfighterCompanion;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var vm = new MainViewModel(window);
            window.DataContext = vm;
            desktop.MainWindow = window;

            // A pilot file passed on the command line (or dropped on the exe) is opened right away.
            var file = desktop.Args?.FirstOrDefault(File.Exists);
            if (file is not null)
            {
                window.Opened += async (_, _) => await vm.OpenPathAsync(file);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
