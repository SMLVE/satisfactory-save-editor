using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Domain.GameData;

namespace SatisfactorySaveEditor.Domain.Inventory;

/// <summary>An item stored in the Dimensional Depot network.</summary>
public sealed record DepotItem(string ItemClass, int Amount, ItemInfo? Item)
{
    public string DisplayName => Item?.DisplayName ?? GameData.GameData.ClassNameOf(ItemClass);
}

/// <summary>
/// The Dimensional Depot's network storage: <c>mStoredItems</c> on FGCentralStorageSubsystem, a list of
/// (item class, amount). Created on first edit when the save has none yet.
/// </summary>
public sealed class DepotService(SaveEditor editor)
{
    private const string StoredItems = "mStoredItems";

    private static PropertyTypeName ItemAmountType =>
        new("StructProperty", new PropertyTypeName("ItemAmount", new PropertyTypeName("/Script/FactoryGame")));

    private SaveObject Subsystem => SaveEditor.Editable(editor.Single("FGCentralStorageSubsystem"));

    public IReadOnlyList<DepotItem> Items =>
        Entries().Select(e => new DepotItem(e.Class, e.Amount.Value, editor.GameData?.FindItem(e.Class))).ToList();

    /// <summary>Sets the amount of an item (adding it if absent). An amount of 0 removes it.</summary>
    public void SetAmount(string item, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        string classPath = ResolveItemPath(item);
        var array = Subsystem.Properties.GetOrAdd(StoredItems, PropertyTypes.ArrayOf(ItemAmountType), () => new ArrayValue(ItemAmountType));
        var existing = Entries().FirstOrDefault(e => e.Class == classPath);
        if (amount == 0)
        {
            if (existing.Struct is not null)
                array.Elements.Remove(existing.Struct);
            return;
        }
        if (existing.Struct is not null)
        {
            existing.Amount.Value = amount;
            return;
        }
        var properties = new PropertyList
        {
            Property.Create("ItemClass", PropertyTypes.Object, new ObjectValue(new ObjectReference(null, classPath))),
            Property.Create("Amount", PropertyTypes.Int, new IntValue(amount)),
        };
        array.Elements.Add(new PropertyListStruct("ItemAmount", properties));
    }

    public void Remove(string item) => SetAmount(item, 0);

    private IEnumerable<(PropertyListStruct Struct, string Class, IntValue Amount)> Entries()
    {
        if (Subsystem.Properties.Get<ArrayValue>(StoredItems) is not { } array)
            yield break;
        foreach (var element in array.Elements.OfType<PropertyListStruct>())
        {
            if (element.Properties.Get<ObjectValue>("ItemClass")?.Value.PathName is not { } path)
                continue;
            var amount = element.Properties.GetOrAdd("Amount", PropertyTypes.Int, () => new IntValue(0));
            yield return (element, path, amount);
        }
    }

    private string ResolveItemPath(string item)
    {
        if (item.StartsWith('/'))
            return item;
        var info = editor.RequireGameData().FindItem(item) ?? throw new ArgumentException($"Unknown item '{item}'.", nameof(item));
        return info.ClassPath ?? throw new ArgumentException($"No asset path is known for '{item}'.", nameof(item));
    }
}
