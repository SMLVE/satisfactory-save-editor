using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Inventory;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.App.ViewModels.Containers;

internal sealed partial class DepotRowViewModel(DepotItem item, Action<DepotRowViewModel> remove) : ObservableObject
{
    public DepotItem Item { get; } = item;
    public string Name => Item.DisplayName;

    [ObservableProperty] private decimal? _amount = item.Amount;

    public bool IsChanged => Amount is { } a && (int)a != Item.Amount;

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>Editor for the Dimensional Depot's network storage (item amounts, not inventory slots).</summary>
internal sealed partial class DepotViewModel : ObservableObject
{
    private readonly SaveDocument _document;

    public DepotViewModel(SaveDocument document, IReadOnlyList<ItemChoice> items)
    {
        _document = document;
        Items = items;
        Reload();
    }

    public IReadOnlyList<ItemChoice> Items { get; }
    public ObservableCollection<DepotRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private ItemChoice? _newItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private decimal? _newAmount = 100;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    public bool IsEmpty => Rows.Count == 0;

    [RelayCommand]
    private void ApplyAmounts()
    {
        var changed = Rows.Where(r => r.IsChanged).ToList();
        foreach (var row in changed)
        {
            int amount = (int)row.Amount!.Value;
            _document.Editor.Depot.SetAmount(row.Item.ItemClass, amount);
            _document.RecordChange($"Dimensional Depot: {row.Name} {row.Item.Amount:N0} → {amount:N0}");
        }
        Show(changed.Count == 0 ? "No amounts changed." : $"Updated {changed.Count} item(s).", false);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var item = NewItem!.Info;
        int amount = (int)NewAmount!.Value;
        int existing = _document.Editor.Depot.Items.FirstOrDefault(i => i.ItemClass == item.ClassPath)?.Amount ?? 0;
        _document.Editor.Depot.SetAmount(item.ClassPath!, existing + amount);
        _document.RecordChange($"Dimensional Depot: added {amount:N0} × {item.DisplayName}");
        Show($"Added {amount:N0} × {item.DisplayName}.", false);
        NewItem = null;
        Reload();
    }

    private bool CanAdd() => NewItem is not null && NewAmount is >= 1;

    private void RemoveRow(DepotRowViewModel row)
    {
        _document.Editor.Depot.Remove(row.Item.ItemClass);
        _document.RecordChange($"Dimensional Depot: removed {row.Item.Amount:N0} × {row.Name}");
        Show($"Removed {row.Name}.", false);
        Reload();
    }

    private void Reload()
    {
        Rows.Clear();
        foreach (var item in _document.Editor.Depot.Items.OrderBy(i => i.DisplayName))
            Rows.Add(new DepotRowViewModel(item, RemoveRow));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Show(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }
}
