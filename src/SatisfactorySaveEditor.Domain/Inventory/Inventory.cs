using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Domain.GameData;

namespace SatisfactorySaveEditor.Domain.Inventory;

/// <summary>One slot of an inventory. <see cref="Item"/> is null when game data is unavailable or the class is unknown.</summary>
public sealed record InventorySlot(int Index, string? ItemClass, int Count, ItemInfo? Item, bool HasState)
{
    public bool IsEmpty => string.IsNullOrEmpty(ItemClass) || Count == 0;
    public string DisplayName => Item?.DisplayName ?? (ItemClass is null ? "(empty)" : GameData.GameData.ClassNameOf(ItemClass));
}

/// <summary>
/// Editable view of an FGInventoryComponent (player inventory, equipment slots, storage containers...).
/// Slots live in <c>mInventoryStacks</c>; <c>mArbitrarySlotSizes</c> and <c>mAllowedItemDescriptors</c>
/// hold per-slot data and are kept the same length.
/// </summary>
public sealed class Inventory
{
    private const string StacksProperty = "mInventoryStacks";
    private const string SlotSizesProperty = "mArbitrarySlotSizes";
    private const string AllowedItemsProperty = "mAllowedItemDescriptors";
    private const string AdjustedSizeDiffProperty = "mAdjustedSizeDiff";

    private readonly SaveEditor _editor;

    internal Inventory(SaveEditor editor, SaveObject component)
    {
        _editor = editor;
        Component = SaveEditor.Editable(component);
        Stacks = Component.Properties.Get<ArrayValue>(StacksProperty)
                 ?? throw new InvalidOperationException($"{component.InstanceName} is not an inventory (no {StacksProperty}).");
    }

    public SaveObject Component { get; }
    private ArrayValue Stacks { get; }

    public int Size => Stacks.Elements.Count;

    public IReadOnlyList<InventorySlot> Slots => Enumerable.Range(0, Size).Select(GetSlot).ToList();

    public InventorySlot GetSlot(int index)
    {
        var (item, count) = StackAt(index);
        return new InventorySlot(index, item.IsEmpty ? null : item.ItemClass, count.Value,
            _editor.GameData?.FindItem(item.ItemClass), item.HasState != 0 || item.LegacyState is { IsEmpty: false });
    }

    /// <summary>
    /// Puts <paramref name="count"/> of an item into a slot. <paramref name="item"/> is a full class path or,
    /// when game data is loaded, a class name such as <c>Desc_IronPlate_C</c>.
    /// </summary>
    public void SetSlot(int index, string item, int count, bool allowOverstack = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        string classPath = ResolveItemPath(item);
        int limit = SlotLimit(index, classPath);
        if (!allowOverstack && limit > 0 && count > limit)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"Stack limit for this slot is {limit}.");

        var (itemStruct, countValue) = StackAt(index);
        if (itemStruct.ItemClass != classPath)
            ResetState(itemStruct);
        itemStruct.ItemClass = classPath;
        countValue.Value = count;
    }

    public void ClearSlot(int index)
    {
        var (itemStruct, countValue) = StackAt(index);
        ResetState(itemStruct);
        itemStruct.ItemClass = null;
        countValue.Value = 0;
    }

    /// <summary>Sets every non-empty slot to its full stack size (requires game data for stack sizes).</summary>
    public int FillStacks()
    {
        int changed = 0;
        for (int i = 0; i < Size; i++)
        {
            var slot = GetSlot(i);
            if (slot.IsEmpty)
                continue;
            int limit = SlotLimit(i, slot.ItemClass!);
            if (limit > 0 && slot.Count < limit)
            {
                StackAt(i).Count.Value = limit;
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// Grows or shrinks the inventory. Shrinking only removes empty trailing slots.
    /// <c>mAdjustedSizeDiff</c> (slots added on top of the class default) is adjusted by the same amount.
    /// </summary>
    public void Resize(int newSize)
    {
        EnsureCanResize(newSize);
        int delta = newSize - Size;
        if (delta == 0)
            return;

        var perSlotArrays = new[] { SlotSizesProperty, AllowedItemsProperty }
            .Select(name => Component.Properties.Get<ArrayValue>(name))
            .Where(a => a is not null && a.Elements.Count == Size)
            .Cast<ArrayValue>()
            .ToList();

        if (delta > 0)
        {
            for (int i = 0; i < delta; i++)
            {
                var stack = PropertyCloner.Clone(Stacks.Elements[0]);
                Stacks.Elements.Add(stack);
                var (item, count) = StackOf(stack);
                ResetState(item);
                item.ItemClass = null;
                count.Value = 0;
                foreach (var array in perSlotArrays)
                    array.Elements.Add(EmptyElement(array));
            }
        }
        else
        {
            Stacks.Elements.RemoveRange(newSize, -delta);
            foreach (var array in perSlotArrays)
                array.Elements.RemoveRange(newSize, -delta);
        }

        var diff = Component.Properties.GetOrAdd(AdjustedSizeDiffProperty, PropertyTypes.Int, () => new IntValue(0));
        diff.Value += delta;
    }

    /// <summary>Throws when <see cref="Resize"/> to <paramref name="newSize"/> would fail, without changing anything.</summary>
    internal void EnsureCanResize(int newSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newSize);
        if (newSize > Size && Size == 0)
            throw new InvalidOperationException("Cannot grow an inventory that has no slot to use as a template.");
        for (int i = newSize; i < Size; i++)
            if (!GetSlot(i).IsEmpty)
                throw new InvalidOperationException($"Cannot shrink to {newSize} slots: slot {i} is not empty.");
    }

    private int SlotLimit(int index, string classPath)
    {
        var sizes = Component.Properties.Get<ArrayValue>(SlotSizesProperty);
        if (sizes is not null && index < sizes.Elements.Count && sizes.Elements[index] is IntValue { Value: > 0 } arbitrary)
            return arbitrary.Value;
        return _editor.GameData?.FindItem(classPath)?.StackSize ?? 0;
    }

    private string ResolveItemPath(string item)
    {
        if (item.StartsWith('/'))
            return item;
        var info = _editor.RequireGameData().FindItem(item) ?? throw new ArgumentException($"Unknown item '{item}'.", nameof(item));
        return info.ClassPath ?? throw new ArgumentException($"No asset path is known for '{item}'.", nameof(item));
    }

    private (InventoryItemStruct Item, IntValue Count) StackAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Size);
        return StackOf(Stacks.Elements[index]);
    }

    private static (InventoryItemStruct Item, IntValue Count) StackOf(PropertyValue element)
    {
        var stack = element as PropertyListStruct ?? throw new InvalidOperationException("Unexpected inventory stack layout.");
        var item = stack.Properties.Get<InventoryItemStruct>("Item") ?? throw new InvalidOperationException("Inventory stack has no Item.");
        var count = stack.Properties.GetOrAdd("NumItems", PropertyTypes.Int, () => new IntValue(0));
        return (item, count);
    }

    private static void ResetState(InventoryItemStruct item)
    {
        item.HasState = 0;
        item.StatePadding = 0;
        item.StateType = null;
        item.State = null;
        item.StateData = [];
        item.LegacyState = ObjectReference.Empty;
    }

    private static PropertyValue EmptyElement(ArrayValue array) => array.ElementType.Name switch
    {
        "IntProperty" => new IntValue(0),
        "ObjectProperty" => new ObjectValue(ObjectReference.Empty),
        _ => throw new InvalidOperationException($"Unexpected per-slot array of {array.ElementType}."),
    };
}
