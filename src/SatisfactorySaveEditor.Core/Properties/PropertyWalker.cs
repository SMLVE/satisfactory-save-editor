namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>Depth-first enumeration of every value nested in a property list.</summary>
public static class PropertyWalker
{
    public static IEnumerable<PropertyValue> Descendants(PropertyList list)
    {
        foreach (var property in list)
            foreach (var value in Descendants(property.Value))
                yield return value;
    }

    public static IEnumerable<PropertyValue> Descendants(PropertyValue value)
    {
        yield return value;
        IEnumerable<PropertyValue> children = value switch
        {
            PropertyListStruct s => Descendants(s.Properties),
            InventoryItemStruct { State: { } state } => Descendants(state),
            ArrayValue a => a.Elements.SelectMany(Descendants),
            SetValue s => s.Removed.Concat(s.Elements).SelectMany(Descendants),
            MapValue m => m.RemovedKeys.Concat(m.Entries.SelectMany(e => new[] { e.Key, e.Value })).SelectMany(Descendants),
            _ => [],
        };
        foreach (var child in children)
            yield return child;
    }
}
