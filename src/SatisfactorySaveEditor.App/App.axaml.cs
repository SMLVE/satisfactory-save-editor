using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels;
using SatisfactorySaveEditor.App.Views;

namespace SatisfactorySaveEditor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new DialogService(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            // Support "Open with" / command line: the first argument is a save to open.
            if (desktop.Args is [var path, ..] && File.Exists(path))
                window.Opened += async (_, _) => await viewModel.OpenPathAsync(Path.GetFullPath(path));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
