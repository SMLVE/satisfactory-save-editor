using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.Domain.GameData;
using SatisfactorySaveEditor.Domain.Inventory;
using SatisfactorySaveEditor.Domain.Players;

namespace SatisfactorySaveEditor.App.ViewModels.Players;

/// <summary>An item offered in the item picker; ToString drives AutoCompleteBox text matching.</summary>
public sealed record ItemChoice(ItemInfo Info)
{
    public override string ToString() => Info.DisplayName;
}

public sealed record InventoryChoice(string Name, Inventory Inventory)
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

public sealed partial class PlayersViewModel : ObservableObject
{
    public PlayersViewModel(SaveDocument document, string? selectPlayer = null)
    {
        var gameData = document.Editor.GameData;
        var items = gameData?.Items.Values.Where(i => i.ClassPath is not null)
            .OrderBy(i => i.DisplayName).Select(i => new ItemChoice(i)).ToList() ?? [];
        HasGameData = gameData is not null;
        Players = document.Editor.Players.All.Select(p => new PlayerViewModel(p, document, items)).ToList();
        SelectedPlayer = Players.FirstOrDefault(p => p.Name == selectPlayer)
                         ?? Players.FirstOrDefault(p => p.HasPawn) ?? Players.FirstOrDefault();
    }

    public bool HasGameData { get; }
    public IReadOnlyList<PlayerViewModel> Players { get; }

    [ObservableProperty] private PlayerViewModel? _selectedPlayer;
}

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly Player _player;
    private readonly SaveDocument _document;

    public PlayerViewModel(Player player, SaveDocument document, IReadOnlyList<ItemChoice> items)
    {
        _player = player;
        _document = document;
        Items = items;
        if (player.Pawn is null)
            return;

        Inventories =
        [
            new InventoryChoice("Inventory", player.Inventory),
            .. player.EquipmentSlots.Select(kv => new InventoryChoice($"{kv.Key} equipment", kv.Value)),
        ];
        _healthText = Format(player.Health);
        var position = player.Position;
        (_positionX, _positionY, _positionZ) = (Format(position.X), Format(position.Y), Format(position.Z));
        SelectedInventory = Inventories[0];
    }

    public string Name => _player.Name;
    public string Detail => _player.Pawn is null ? "No character in this save" : $"Health {Format(_player.Health)}";
    public bool HasPawn => _player.Pawn is not null;
    public IReadOnlyList<ItemChoice> Items { get; }
    public IReadOnlyList<InventoryChoice> Inventories { get; } = [];
    public ObservableCollection<SlotViewModel> Slots { get; } = [];

    [ObservableProperty] private string _healthText = "";
    [ObservableProperty] private string _positionX = "";
    [ObservableProperty] private string _positionY = "";
    [ObservableProperty] private string _positionZ = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    [ObservableProperty] private InventoryChoice? _selectedInventory;

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

    [RelayCommand]
    private void ApplyHealth() => Try(() =>
    {
        _player.Health = ParseFloat(HealthText, "Health");
        HealthText = Format(_player.Health);
        OnPropertyChanged(nameof(Detail));
        return ($"Health set to {HealthText}.", true);
    });

    [RelayCommand]
    private void ApplyPosition() => Try(() =>
    {
        _player.Position = new Vector3(ParseFloat(PositionX, "X"), ParseFloat(PositionY, "Y"), ParseFloat(PositionZ, "Z"));
        return ("Position updated.", true);
    });

    [RelayCommand(CanExecute = nameof(CanSetSlot))]
    private void SetSlot() => Try(() =>
    {
        var slot = SelectedSlot!.Slot;
        SelectedInventory!.Inventory.SetSlot(slot.Index, EditItem!.Info.ClassPath!, (int)EditCount!.Value, AllowOverstack);
        ReloadSlots(slot.Index);
        return ($"Slot {slot.Index + 1}: {EditCount} × {EditItem.Info.DisplayName}.", true);
    });

    private bool CanSetSlot() => SelectedSlot is not null && EditItem is not null && EditCount is >= 1;

    [RelayCommand(CanExecute = nameof(HasSelectedSlot))]
    private void ClearSlot() => Try(() =>
    {
        int index = SelectedSlot!.Slot.Index;
        SelectedInventory!.Inventory.ClearSlot(index);
        ReloadSlots(index);
        return ($"Slot {index + 1} cleared.", true);
    });

    [RelayCommand]
    private void FillStacks() => Try(() =>
    {
        int changed = SelectedInventory!.Inventory.FillStacks();
        ReloadSlots(SelectedSlot?.Slot.Index);
        return (changed == 0 ? "All stacks are already full." : $"Filled {changed} stacks.", changed > 0);
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

    /// <summary>Runs an edit, shows its message and marks the save dirty when something changed.</summary>
    private void Try(Func<(string Message, bool Changed)> edit)
    {
        try
        {
            var (message, changed) = edit();
            Message = message;
            MessageIsError = false;
            if (changed)
                _document.MarkDirty();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            Message = ex is ArgumentOutOfRangeException range ? range.Message.Split(" (Parameter")[0] : ex.Message;
            MessageIsError = true;
        }
    }

    private static float ParseFloat(string text, string field) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value
            : throw new FormatException($"{field} must be a number.");

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
