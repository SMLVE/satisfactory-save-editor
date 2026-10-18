using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SatisfactorySaveEditor.App.Controls;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Inventory;
using SatisfactorySaveEditor.App.ViewModels.Players;
using SatisfactorySaveEditor.Domain.Map;

namespace SatisfactorySaveEditor.App.ViewModels.Map;

/// <summary>A layer toggle with its marker color and count.</summary>
public sealed partial class MapLayer(MapMarkerKind kind, string label, int count, bool visible, Action changed) : ObservableObject
{
    public MapMarkerKind Kind { get; } = kind;
    public string Label { get; } = $"{label} ({count:N0})";
    public IBrush Brush => MapControl.BrushFor(Kind);

    [ObservableProperty] private bool _isVisible = visible;

    partial void OnIsVisibleChanged(bool value) => changed();
}

public sealed partial class MapViewModel : ObservableObject
{
    private readonly SaveDocument _document;
    private readonly IReadOnlyList<MapMarker> _all;
    private readonly IReadOnlyList<ItemChoice> _items;

    public MapViewModel(SaveDocument document)
    {
        _document = document;
        _items = ItemChoice.From(document.Editor.GameData);
        _all = document.Editor.Map.Markers;
        int Count(MapMarkerKind k) => _all.Count(m => m.Kind == k);
        Layers =
        [
            new(MapMarkerKind.Player, "Players", Count(MapMarkerKind.Player), true, ApplyFilter),
            new(MapMarkerKind.Storage, "Storage", Count(MapMarkerKind.Storage), true, ApplyFilter),
            new(MapMarkerKind.Vehicle, "Vehicles", Count(MapMarkerKind.Vehicle), true, ApplyFilter),
            new(MapMarkerKind.World, "Crates & drop pods", Count(MapMarkerKind.World), true, ApplyFilter),
            new(MapMarkerKind.Machine, "Machines", Count(MapMarkerKind.Machine), true, ApplyFilter),
            new(MapMarkerKind.Logistics, "Splitters & mergers", Count(MapMarkerKind.Logistics), false, ApplyFilter),
            new(MapMarkerKind.Building, "Other buildings", Count(MapMarkerKind.Building), true, ApplyFilter),
        ];
        ApplyFilter();
    }

    public IReadOnlyList<MapLayer> Layers { get; }

    [ObservableProperty] private IReadOnlyList<MapMarker> _visibleMarkers = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Title), nameof(Subtitle), nameof(HasNoInventory))]
    private MapMarker? _selectedMarker;

    /// <summary>Editor for the selected player (health, position, inventories).</summary>
    [ObservableProperty] private PlayerViewModel? _player;
    /// <summary>Editor for the selected container's inventories.</summary>
    [ObservableProperty] private InventoryEditorViewModel? _inventory;

    public bool HasSelection => SelectedMarker is not null;
    public bool HasNoInventory => SelectedMarker is { Player: null, Container: null };
    public string Title => SelectedMarker?.Label ?? "";

    public string Subtitle
    {
        get
        {
            if (SelectedMarker is not { } m)
                return "";
            var p = m.Position;
            string where = string.Create(CultureInfo.InvariantCulture, $"({p.X / 100:0}, {p.Y / 100:0}, {p.Z / 100:0}) m");
            string name = m.Actor.InstanceName?[(m.Actor.InstanceName.LastIndexOf('.') + 1)..] ?? "";
            return m.Container is { } c ? $"{name} · {c.UsedSlots}/{c.TotalSlots} slots used · {where}" : $"{name} · {where}";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Rebuilds the detail editors so they reflect edits made in other tabs. Markers keep their positions
    /// (only player position edits move one, and the player detail shows the edited value).
    /// </summary>
    public void RefreshSelection() => OnSelectedMarkerChanged(SelectedMarker);

    partial void OnSelectedMarkerChanged(MapMarker? value)
    {
        Player = value?.Player is { } player ? new PlayerViewModel(player, _document, _items) : null;
        Inventory = value?.Container is { } container
            ? new InventoryEditorViewModel(
                container.Inventories.Select(i => new InventoryChoice(i.Name, i.Inventory)).ToList(), _items,
                change => _document.RecordChange($"{container}: {change}"))
            : null;
    }

    private void ApplyFilter()
    {
        var visible = Layers.Where(l => l.IsVisible).Select(l => l.Kind).ToHashSet();
        var search = SearchText.Trim();
        VisibleMarkers = _all.Where(m => visible.Contains(m.Kind)
                                         && (search.Length == 0
                                             || m.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                                             || (m.Actor.InstanceName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                                             || HoldsItem(m, search)))
            .ToList();
        Summary = $"{VisibleMarkers.Count:N0} of {_all.Count:N0} objects shown";
    }

    /// <summary>Search also matches containers and players carrying an item, e.g. "Motor".</summary>
    private static bool HoldsItem(MapMarker marker, string search)
    {
        var inventories = marker.Container?.Inventories.Select(i => i.Inventory)
                          ?? (marker.Player is { Pawn: not null } p ? [p.Inventory] : []);
        return inventories.Any(inv => inv.Slots.Any(s => !s.IsEmpty && s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
    }
}
