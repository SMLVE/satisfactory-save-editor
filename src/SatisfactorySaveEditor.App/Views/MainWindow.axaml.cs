using Avalonia.Controls;
using Avalonia.Interactivity;
using SatisfactorySaveEditor.App.ViewModels;

namespace SatisfactorySaveEditor.App.Views;

internal sealed partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow() => InitializeComponent();

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainWindowViewModel vm)
            return;
        // Closing must be cancelled synchronously; ask asynchronously and close again if confirmed.
        e.Cancel = true;
        if (await vm.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
