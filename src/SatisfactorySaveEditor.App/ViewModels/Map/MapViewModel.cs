using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Controls;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Inventory;
using SatisfactorySaveEditor.App.ViewModels.Players;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Map;
using SatisfactorySaveEditor.Domain.Structures;

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
    private readonly IDialogService _dialogs;
    private readonly IReadOnlyList<MapMarker> _all;
    private readonly IReadOnlyList<ItemChoice> _items;

    public MapViewModel(SaveDocument document, IDialogService dialogs)
    {
        _document = document;
        _dialogs = dialogs;
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
            new(MapMarkerKind.Conveyor, "Belts & pipes", Count(MapMarkerKind.Conveyor), true, ApplyFilter),
            new(MapMarkerKind.Logistics, "Splitters & mergers", Count(MapMarkerKind.Logistics), false, ApplyFilter),
            new(MapMarkerKind.Building, "Other buildings", Count(MapMarkerKind.Building), true, ApplyFilter),
        ];
        _powerLines = document.Editor.Map.PowerLines;
        PowerLinesLabel = $"Power lines ({_powerLines.Count:N0})";
        _structures = document.Editor.Structures.All;
        StructuresLabel = document.Editor.Structures.IsUndecoded
            ? "Foundations & walls (not readable in this save)"
            : $"Foundations & walls ({_structures.Count:N0})";
        ApplyFilter();
    }

    private readonly IReadOnlyList<MapLine> _powerLines;
    private IReadOnlyList<Structure> _structures;

    // ---- Box selection (Shift+drag) ----

    private static readonly IReadOnlySet<MapMarker> NoMarkers = new HashSet<MapMarker>();
    private static readonly IReadOnlySet<Structure> NoStructures = new HashSet<Structure>();
    private bool _selectingBox;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBoxSelection), nameof(BoxSummary), nameof(BoxHasStructures), nameof(ShowNothingSelected))]
    private IReadOnlySet<MapMarker> _boxMarkers = NoMarkers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBoxSelection), nameof(BoxSummary), nameof(BoxHasStructures), nameof(ShowNothingSelected))]
    private IReadOnlySet<Structure> _boxStructures = NoStructures;

    [ObservableProperty] private string _moveX = "0";
    [ObservableProperty] private string _moveY = "0";
    [ObservableProperty] private string _moveZ = "0";
    [ObservableProperty] private string? _boxMessage;
    [ObservableProperty] private bool _boxMessageIsError;

    public bool HasBoxSelection => BoxMarkers.Count + BoxStructures.Count > 0;
    public bool BoxHasStructures => BoxStructures.Count > 0;

    /// <summary>Selected objects counted by type, e.g. "120 × Foundation 8x4 01".</summary>
    public IReadOnlyList<string> BoxSummary =>
        BoxStructures.GroupBy(s => s.TypeName).Select(g => (g.Key, Count: g.Count()))
            .Concat(BoxMarkers.GroupBy(m => m.Label).Select(g => (g.Key, Count: g.Count())))
            .OrderByDescending(g => g.Count)
            .Select(g => $"{g.Count:N0} × {g.Key}")
            .ToList();

    /// <summary>Selects every visible marker and structure inside a world rectangle (from a Shift+drag).</summary>
    public void SelectBox(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
    {
        bool Inside(System.Numerics.Vector3 p) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
        BoxMarkers = VisibleMarkers.Where(m => m.Path is { } path ? path.Any(Inside) : Inside(m.Position)).ToHashSet();
        BoxStructures = VisibleStructures.Where(s => Inside(s.Position)).ToHashSet();
        BoxMessage = null;
        _selectingBox = true;
        SelectedMarker = null;
        _selectingBox = false;
    }

    [RelayCommand]
    private void ClearBoxSelection()
    {
        BoxMarkers = NoMarkers;
        BoxStructures = NoStructures;
        BoxMessage = null;
    }

    /// <summary>Deletes the whole box selection after one summarized preview. The map rebuilds afterwards.</summary>
    [RelayCommand]
    private Task DeleteBoxSelectionAsync() =>
        DeleteWorkflow.RunSelectionAsync(_document, _dialogs, BoxMarkers.Select(m => m.Actor).ToList(), BoxStructures.ToList());

    /// <summary>
    /// Moves the selected foundations/walls by an offset in metres. Buildings are not moved: their belts
    /// and wires would be left behind.
    /// </summary>
    [RelayCommand]
    private void MoveStructures()
    {
        if (!TryMetres(MoveX, out float x) || !TryMetres(MoveY, out float y) || !TryMetres(MoveZ, out float z))
        {
            BoxMessage = "Offsets must be numbers (metres).";
            BoxMessageIsError = true;
            return;
        }
        if (x == 0 && y == 0 && z == 0)
            return;
        var selected = BoxStructures.ToList();
        _document.Editor.Structures.Move(selected, new System.Numerics.Vector3(x * 100, y * 100, z * 100));
        _document.RecordChange(string.Create(CultureInfo.InvariantCulture,
            $"Moved {selected.Count} foundations/walls by ({x:0.##}, {y:0.##}, {z:0.##}) m"));

        // Re-read structures (positions changed) and keep the same instances selected.
        var instances = selected.Select(s => s.Instance).ToHashSet();
        _structures = _document.Editor.Structures.All;
        VisibleStructures = ShowStructures ? _structures : [];
        BoxStructures = _structures.Where(s => instances.Contains(s.Instance)).ToHashSet();
        BoxMessage = string.Create(CultureInfo.InvariantCulture, $"Moved {selected.Count} by ({x:0.##}, {y:0.##}, {z:0.##}) m.")
                     + (BoxMarkers.Count > 0 ? $" {BoxMarkers.Count} selected buildings were not moved." : "");
        BoxMessageIsError = false;
    }

    private static bool TryMetres(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    public string StructuresLabel { get; }
    public IBrush StructuresBrush => MapControl.StructureBrush;

    [ObservableProperty] private bool _showStructures = true;
    [ObservableProperty] private IReadOnlyList<Structure> _visibleStructures = [];

    partial void OnShowStructuresChanged(bool value) => VisibleStructures = value ? _structures : [];

    public IReadOnlyList<MapLayer> Layers { get; }
    public string PowerLinesLabel { get; }
    public IBrush PowerLinesBrush => MapControl.PowerLineBrush;

    [ObservableProperty] private bool _showPowerLines = true;
    [ObservableProperty] private IReadOnlyList<MapLine> _visibleLines = [];

    /// <summary>What the selected building is wired to, e.g. "Power Pole Mk1 ↔ Constructor Mk1".</summary>
    public IReadOnlyList<string> PowerConnections =>
        SelectedMarker is { } m
            ? _powerLines.Where(l => l.FromBuilding == m.Actor || l.ToBuilding == m.Actor || l.Line == m.Actor)
                .Select(l => $"{Friendly(l.FromBuilding)} ↔ {Friendly(l.ToBuilding)}").ToList()
            : [];

    public bool HasPowerConnections => PowerConnections.Count > 0;

    /// <summary>Items on the selected belt, e.g. "16 × Coal" (empty for other objects).</summary>
    public IReadOnlyList<string> BeltContents =>
        SelectedMarker is { } m && m.Actor.Trailing is ConveyorItemsTrailing
            ? _document.Editor.Conveyors.ContentsOf(m.Actor)
                .Select(c => $"{c.Count} × {_document.Editor.GameData?.FindItem(c.ItemClass)?.DisplayName ?? Domain.GameData.GameData.ClassNameOf(c.ItemClass)}")
                .DefaultIfEmpty("Empty").ToList()
            : [];

    public bool IsBelt => SelectedMarker?.Actor.Trailing is ConveyorItemsTrailing;

    partial void OnShowPowerLinesChanged(bool value) => VisibleLines = value ? _powerLines : [];

    private static string Friendly(Core.Model.ActorObject actor) => Domain.Inventory.ContainerService.FriendlyClassName(actor.ClassName);

    [ObservableProperty] private IReadOnlyList<MapMarker> _visibleMarkers = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Title), nameof(Subtitle), nameof(HasNoInventory),
        nameof(PowerConnections), nameof(HasPowerConnections), nameof(BeltContents), nameof(IsBelt), nameof(ShowNothingSelected))]
    private MapMarker? _selectedMarker;

    /// <summary>Editor for the selected player (health, position, inventories).</summary>
    [ObservableProperty] private PlayerViewModel? _player;
    /// <summary>Editor for the selected container's inventories.</summary>
    [ObservableProperty] private InventoryEditorViewModel? _inventory;

    public bool HasSelection => SelectedMarker is not null;
    public bool ShowNothingSelected => !HasSelection && !HasBoxSelection;
    public bool HasNoInventory => SelectedMarker is { Player: null, Container: null } && !IsBelt;
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

    /// <summary>Deletes the selected object after showing what will be removed. The map rebuilds afterwards.</summary>
    [RelayCommand]
    private Task DeleteSelectedAsync() =>
        SelectedMarker is { } marker ? DeleteWorkflow.RunAsync(_document, _dialogs, marker.Actor) : Task.CompletedTask;

    /// <summary>
    /// Rebuilds the detail editors so they reflect edits made in other tabs. Markers keep their positions
    /// (only player position edits move one, and the player detail shows the edited value).
    /// </summary>
    public void RefreshSelection() => OnSelectedMarkerChanged(SelectedMarker);

    partial void OnSelectedMarkerChanged(MapMarker? value)
    {
        // Clicking a single object replaces a box selection.
        if (value is not null && !_selectingBox && HasBoxSelection)
            ClearBoxSelection();
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
        VisibleLines = ShowPowerLines ? _powerLines : [];
        VisibleStructures = ShowStructures ? _structures : [];
    }

    /// <summary>Search also matches containers and players carrying an item, e.g. "Motor".</summary>
    private static bool HoldsItem(MapMarker marker, string search)
    {
        var inventories = marker.Container?.Inventories.Select(i => i.Inventory)
                          ?? (marker.Player is { Pawn: not null } p ? [p.Inventory] : []);
        return inventories.Any(inv => inv.Slots.Any(s => !s.IsEmpty && s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
    }
}
