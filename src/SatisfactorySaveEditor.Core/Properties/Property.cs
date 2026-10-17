namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>A tagged property: header plus value.</summary>
public sealed class Property(PropertyTag tag, PropertyValue value)
{
    public PropertyTag Tag { get; } = tag;
    public PropertyValue Value { get; set; } = value;

    public string? Name => Tag.Name;
    public PropertyTypeName Type => Tag.Type;

    public override string ToString() =>
        Tag.ArrayIndex == 0 ? $"{Name} = {Value}" : $"{Name}[{Tag.ArrayIndex}] = {Value}";
}

/// <summary>An ordered list of tagged properties, terminated on disk by a "None" name.</summary>
public sealed class PropertyList : List<Property>
{
    public Property? Find(string name, int arrayIndex = 0) =>
        this.FirstOrDefault(p => p.Name == name && p.Tag.ArrayIndex == arrayIndex);

    public T? Get<T>(string name, int arrayIndex = 0) where T : PropertyValue =>
        Find(name, arrayIndex)?.Value as T;
}
