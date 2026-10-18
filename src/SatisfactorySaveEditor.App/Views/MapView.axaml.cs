using Avalonia.Controls;
using Avalonia.Interactivity;
using SatisfactorySaveEditor.App.ViewModels.Map;

namespace SatisfactorySaveEditor.App.Views;

internal sealed partial class MapView : UserControl
{
    public MapView()
    {
        InitializeComponent();
        Map.BoxSelected += (_, box) => (DataContext as MapViewModel)?.SelectBox(box.Min, box.Max);
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => Map.FitToMarkers();
}
