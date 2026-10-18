using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.Domain.GameData;
using SatisfactorySaveEditor.Domain.Progression;

namespace SatisfactorySaveEditor.App.ViewModels.Progression;

public sealed record SchematicTypeFilter(string Label, Func<SchematicInfo, bool> Matches)
{
    public override string ToString() => Label;
}

public sealed partial class SchematicRow(SchematicInfo info, bool isPurchased) : ObservableObject
{
    public SchematicInfo Info { get; } = info;
    public string Name => Info.DisplayName.Length > 0 ? Info.DisplayName : Info.ClassName;
    public string TypeLabel => ProgressionViewModel.TypeLabel(Info.Type);
    public string TierText => Info.TechTier > 0 ? $"Tier {Info.TechTier}" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool _isPurchased = isPurchased;

    [ObservableProperty] private bool _isChecked;

    public bool CanCheck => !IsPurchased;
}

public sealed partial class ProgressionViewModel : ObservableObject
{
    private readonly SaveDocument _document;
    private readonly List<SchematicRow> _allRows = [];

    public ProgressionViewModel(SaveDocument document)
    {
        _document = document;
        HasGameData = document.Editor.GameData is not null;
        TypeFilters =
        [
            new("Milestones", s => s.IsMilestone),
            new("MAM research", s => s.IsMam),
            new("Alternate recipes", s => s.IsAlternate),
            new("AWESOME Shop", s => s.IsShop),
            new("All schematics", _ => true),
        ];
        if (HasGameData)
        {
            var progression = document.Editor.Progression;
            _allRows.AddRange(document.Editor.GameData!.Schematics.Values
                .Where(s => s.ClassPath is not null)
                .OrderBy(s => s.TechTier).ThenBy(s => s.DisplayName)
                .Select(s => new SchematicRow(s, progression.IsPurchased(s.ClassName))));
        }
        SelectedFilter = TypeFilters[0]; // triggers the initial filter
        RefreshSummary();
    }

    /// <summary>Re-reads purchased state and slot counts (after edits made in other tabs).</summary>
    public void Refresh()
    {
        if (!HasGameData)
            return;
        var progression = _document.Editor.Progression;
        foreach (var row in _allRows)
            row.IsPurchased = progression.IsPurchased(row.Info.ClassName);
        RefreshSummary();
    }

    public bool HasGameData { get; }
    public IReadOnlyList<SchematicTypeFilter> TypeFilters { get; }
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] private IReadOnlyList<SchematicRow> _rows = [];
    [ObservableProperty] private SchematicTypeFilter _selectedFilter = null!;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private decimal? _inventorySlots;
    [ObservableProperty] private decimal? _handSlots;
    [ObservableProperty] private string? _error;

    partial void OnSelectedFilterChanged(SchematicTypeFilter value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void UnlockChecked() => Unlock(Rows.Where(r => r.IsChecked && !r.IsPurchased).ToList());

    [RelayCommand]
    private void UnlockAllShown() => Unlock(Rows.Where(r => !r.IsPurchased).ToList());

    [RelayCommand]
    private void ApplySlots()
    {
        Error = null;
        try
        {
            var progression = _document.Editor.Progression;
            if (InventorySlots is { } inv && (int)inv != progression.InventorySlots)
            {
                int before = progression.InventorySlots;
                progression.SetInventorySlots((int)inv);
                Log.Insert(0, $"Inventory slots set to {(int)inv}.");
                _document.RecordChange($"Inventory slots {before} → {(int)inv}");
            }
            if (HandSlots is { } hands && (int)hands != progression.ArmEquipmentSlots)
            {
                int before = progression.ArmEquipmentSlots;
                progression.SetArmEquipmentSlots((int)hands);
                Log.Insert(0, $"Hand slots set to {(int)hands}.");
                _document.RecordChange($"Hand slots {before} → {(int)hands}");
            }
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        RefreshSummary();
    }

    private void Unlock(IReadOnlyList<SchematicRow> rows)
    {
        Error = null;
        if (rows.Count == 0)
        {
            Error = "Nothing to unlock.";
            return;
        }
        var skipped = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            UnlockResult result;
            try
            {
                result = _document.Editor.Progression.Unlock(row.Info.ClassName);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Log.Insert(0, $"{row.Name}: failed — {ex.Message}");
                continue;
            }
            row.IsPurchased = true;
            row.IsChecked = false;
            var applied = string.Join(", ", result.Applied.DefaultIfEmpty("unlocked"));
            Log.Insert(0, $"{row.Name}: {applied}");
            _document.RecordChange($"Unlocked {row.TypeLabel.ToLowerInvariant()} \"{row.Name}\" ({applied})");
            foreach (var kind in result.Skipped)
                skipped[kind] = skipped.GetValueOrDefault(kind) + 1;
        }
        foreach (var (kind, count) in skipped)
            Log.Insert(0, $"Not applied ({count}×): {DescribeSkipped(kind)}");
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        if (!HasGameData)
        {
            Summary = "Game data is required to list schematics.";
            return;
        }
        var progression = _document.Editor.Progression;
        Summary = $"{progression.PurchasedSchematics.Count} schematics purchased · {progression.AvailableRecipes.Count} recipes available";
        InventorySlots = progression.InventorySlots;
        HandSlots = progression.ArmEquipmentSlots;
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Rows = _allRows.Where(r => SelectedFilter.Matches(r.Info)
                                   && (search.Length == 0 || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    internal static string TypeLabel(string type) => type switch
    {
        "EST_Milestone" => "Milestone",
        "EST_MAM" => "MAM",
        "EST_Alternate" => "Alternate",
        "EST_ResourceSink" => "Shop",
        "EST_Tutorial" => "Tutorial",
        "EST_Customization" => "Customization",
        "EST_HardDrive" => "Hard drive",
        _ => "Other",
    };

    private static string DescribeSkipped(string kind) => kind switch
    {
        "BP_UnlockGiveItem_C" => "items given on purchase",
        "BP_UnlockSchematic_C" => "other schematics made available",
        "BP_UnlockScannableResource_C" => "resource scanner entries",
        "BP_UnlockScannableObject_C" => "object scanner entries",
        "FGUnlockCustomization" or "BP_UnlockCustomizer_C" => "customizer unlocks",
        "FGUnlockTape" => "boombox tapes",
        _ => kind,
    };
}
