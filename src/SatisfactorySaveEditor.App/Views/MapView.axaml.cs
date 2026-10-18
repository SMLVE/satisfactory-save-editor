using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SatisfactorySaveEditor.App.Views;

public partial class MapView : UserControl
{
    public MapView() => InitializeComponent();

    private void OnFitClick(object? sender, RoutedEventArgs e) => Map.FitToMarkers();
}
