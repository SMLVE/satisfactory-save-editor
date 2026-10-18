using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Domain.GameData;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.App.ViewModels.Inventory;

/// <summary>An item offered in the item picker; ToString drives AutoCompleteBox text matching.</summary>
public sealed record ItemChoice(ItemInfo Info)
{
    public override string ToString() => Info.DisplayName;

    public static IReadOnlyList<ItemChoice> From(GameData? gameData) =>
        gameData?.Items.Values.Where(i => i.ClassPath is not null)
            .OrderBy(i => i.DisplayName).Select(i => new ItemChoice(i)).ToList() ?? [];
}

public sealed record InventoryChoice(string Name, Domain.Inventory.Inventory Inventory)
{
    public override string ToString() => $"{Name} ({Inventory.Size} slots)";
}

public sealed record SlotViewModel(InventorySlot Slot)
{
    public string Number => (Slot.Index + 1).ToString(CultureInfo.InvariantCulture);
    public string Name => Slot.IsEmpty ? "Empty" : Slot.DisplayName;
    public string CountText => Slot.IsEmpty ? "" : $"× {Slot.Count}";
    public bool IsEmpty => Slot.IsEmpty;
}

/// <summary>
/// Slot grid plus slot editor for one or more inventories of the same owner (a player, a container,
/// a vehicle...). Edits are reported through <paramref name="recordChange"/> for the change log.
/// </summary>
public sealed partial class InventoryEditorViewModel : ObservableObject
{
    private readonly Action<string> _recordChange;

    public InventoryEditorViewModel(IReadOnlyList<InventoryChoice> inventories, IReadOnlyList<ItemChoice> items,
        Action<string> recordChange)
    {
        Inventories = inventories;
        Items = items;
        _recordChange = recordChange;
        SelectedInventory = inventories.FirstOrDefault();
    }

    public IReadOnlyList<InventoryChoice> Inventories { get; }
    public IReadOnlyList<ItemChoice> Items { get; }
    public bool HasItems => Items.Count > 0;
    public ObservableCollection<SlotViewModel> Slots { get; } = [];

    [ObservableProperty] private InventoryChoice? _selectedInventory;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSlot))]
    [NotifyCanExecuteChangedFor(nameof(SetSlotCommand), nameof(ClearSlotCommand))]
    private SlotViewModel? _selectedSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxCount), nameof(StackHint))]
    [NotifyCanExecuteChangedFor(nameof(SetSlotCommand))]
    private ItemChoice? _editItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetSlotCommand))]
    private decimal? _editCount = 1;

    [ObservableProperty] private bool _allowOverstack;

    public bool HasSelectedSlot => SelectedSlot is not null;
    public decimal MaxCount => AllowOverstack ? int.MaxValue : EditItem?.Info.StackSize ?? int.MaxValue;
    public string StackHint => EditItem is null ? "" : $"Stack size {EditItem.Info.StackSize}";

    partial void OnAllowOverstackChanged(bool value) => OnPropertyChanged(nameof(MaxCount));

    partial void OnSelectedInventoryChanged(InventoryChoice? value) => ReloadSlots();

    partial void OnSelectedSlotChanged(SlotViewModel? value)
    {
        if (value is null)
            return;
        EditItem = value.Slot.IsEmpty ? null : Items.FirstOrDefault(i => i.Info.ClassPath == value.Slot.ItemClass);
        EditCount = value.Slot.IsEmpty ? EditItem?.Info.StackSize ?? 1 : value.Slot.Count;
    }

    [RelayCommand(CanExecute = nameof(CanSetSlot))]
    private void SetSlot() => Try(() =>
    {
        var slot = SelectedSlot!.Slot;
        SelectedInventory!.Inventory.SetSlot(slot.Index, EditItem!.Info.ClassPath!, (int)EditCount!.Value, AllowOverstack);
        ReloadSlots(slot.Index);
        return ($"{SelectedInventory.Name} slot {slot.Index + 1}: {EditCount} × {EditItem.Info.DisplayName}.", true);
    });

    private bool CanSetSlot() => SelectedSlot is not null && EditItem is not null && EditCount is >= 1;

    [RelayCommand(CanExecute = nameof(HasSelectedSlot))]
    private void ClearSlot() => Try(() =>
    {
        int index = SelectedSlot!.Slot.Index;
        SelectedInventory!.Inventory.ClearSlot(index);
        ReloadSlots(index);
        return ($"{SelectedInventory.Name} slot {index + 1} cleared.", true);
    });

    [RelayCommand]
    private void FillStacks() => Try(() =>
    {
        int changed = SelectedInventory!.Inventory.FillStacks();
        ReloadSlots(SelectedSlot?.Slot.Index);
        return (changed == 0 ? "All stacks are already full." : $"{SelectedInventory.Name}: filled {changed} stacks.", changed > 0);
    });

    private void ReloadSlots(int? reselect = null)
    {
        Slots.Clear();
        if (SelectedInventory is null)
            return;
        foreach (var slot in SelectedInventory.Inventory.Slots)
            Slots.Add(new SlotViewModel(slot));
        SelectedSlot = reselect is { } i && i < Slots.Count ? Slots[i] : null;
    }

    private void Try(Func<(string Message, bool Changed)> edit)
    {
        try
        {
            var (message, changed) = edit();
            Message = message;
            MessageIsError = false;
            if (changed)
                _recordChange(message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Message = ex is ArgumentOutOfRangeException range ? range.Message.Split(" (Parameter")[0] : ex.Message;
            MessageIsError = true;
        }
    }
}
