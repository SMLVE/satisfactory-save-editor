using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Inventory;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.App.ViewModels.Containers;

public sealed record CategoryFilter(string Label, ContainerCategory? Category, bool IsDepot = false)
{
    public override string ToString() => Label;
}

public sealed record ContainerRow(Container Container)
{
    public string Title => Container.TypeName;
    public string Detail
    {
        get
        {
            var p = Container.Position;
            // Some actors (e.g. parked vehicles) have no stored transform and report the origin.
            var where = p == System.Numerics.Vector3.Zero
                ? "position not stored"
                : string.Create(CultureInfo.InvariantCulture, $"({p.X / 100:0}, {p.Y / 100:0}, {p.Z / 100:0}) m");
            return $"{Container.UsedSlots}/{Container.TotalSlots} slots used · {where}";
        }
    }
}

/// <summary>Storage containers, vehicles, machines and world crates, each edited with the shared inventory editor.</summary>
public sealed partial class ContainersViewModel : ObservableObject
{
    private readonly SaveDocument _document;
    private readonly IDialogService _dialogs;
    private readonly IReadOnlyList<ItemChoice> _items;
    private readonly List<ContainerRow> _all;

    public ContainersViewModel(SaveDocument document, IDialogService dialogs)
    {
        _document = document;
        _dialogs = dialogs;
        _items = ItemChoice.From(document.Editor.GameData);
        _all = document.Editor.Containers.All.Select(c => new ContainerRow(c)).ToList();
        Filters =
        [
            new("Storage", ContainerCategory.Storage),
            new("Vehicles", ContainerCategory.Vehicles),
            new("Machines", ContainerCategory.Machines),
            new("Splitters & mergers", ContainerCategory.Logistics),
            new("Crates & drop pods", ContainerCategory.World),
            new("Everything", null),
            new("Dimensional Depot (network storage)", null, IsDepot: true),
        ];
        SelectedFilter = Filters[0];
    }

    /// <summary>Editor for the Dimensional Depot, shown instead of the container list when that filter is picked.</summary>
    [ObservableProperty] private DepotViewModel? _depot;

    public bool ShowDepot => Depot is not null;
    public bool ShowContainers => Depot is null;

    partial void OnDepotChanged(DepotViewModel? value)
    {
        OnPropertyChanged(nameof(ShowDepot));
        OnPropertyChanged(nameof(ShowContainers));
    }

    public IReadOnlyList<CategoryFilter> Filters { get; }

    [ObservableProperty] private CategoryFilter _selectedFilter = null!;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<ContainerRow> _rows = [];
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private ContainerRow? _selectedRow;
    [ObservableProperty] private InventoryEditorViewModel? _inventory;

    /// <summary>Deletes the selected container after showing what will be removed. The tab rebuilds afterwards.</summary>
    [RelayCommand]
    private Task DeleteSelectedAsync() =>
        SelectedRow is { } row ? DeleteWorkflow.RunAsync(_document, _dialogs, row.Container.Owner) : Task.CompletedTask;

    partial void OnSelectedFilterChanged(CategoryFilter value)
    {
        Depot = value.IsDepot ? new DepotViewModel(_document, _items) : null;
        ApplyFilter();
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedRowChanged(ContainerRow? value)
    {
        Inventory = value is null
            ? null
            : new InventoryEditorViewModel(
                value.Container.Inventories.Select(i => new InventoryChoice(i.Name, i.Inventory)).ToList(),
                _items,
                change => _document.RecordChange($"{value.Container}: {change}"));
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Rows = _all.Where(r => !SelectedFilter.IsDepot
                               && (SelectedFilter.Category is null || r.Container.Category == SelectedFilter.Category)
                               && (search.Length == 0
                                   || r.Container.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || r.Container.InstanceName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || ContainsItem(r.Container, search)))
            .ToList();
        Summary = $"{Rows.Count:N0} of {_all.Count:N0}";
    }

    /// <summary>Lets the search box also find containers holding a given item, e.g. "Motor".</summary>
    private static bool ContainsItem(Container container, string search) =>
        container.Inventories.Any(i => i.Inventory.Slots.Any(s => !s.IsEmpty && s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
}
