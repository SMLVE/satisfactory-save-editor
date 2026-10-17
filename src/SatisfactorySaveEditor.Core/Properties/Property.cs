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

    /// <summary>
    /// Creates a new property with a fresh tag. Used when a value was omitted from the save because it
    /// equalled the class default. Native structs get the binary-serialization flag modern tags require.
    /// </summary>
    public static Property Create(string name, PropertyTypeName type, PropertyValue value)
    {
        var tag = new PropertyTag { Name = name, Type = type };
        if (type.Name == "StructProperty" && value is not PropertyListStruct)
            tag.Flags |= PropertyTagFlags.HasBinaryOrNativeSerialize;
        return new Property(tag, value);
    }
}

/// <summary>Type names for properties the editor creates.</summary>
public static class PropertyTypes
{
    public static PropertyTypeName Int => new("IntProperty");
    public static PropertyTypeName Int64 => new("Int64Property");
    public static PropertyTypeName Float => new("FloatProperty");
    public static PropertyTypeName Bool => new("BoolProperty");
    public static PropertyTypeName Object => new("ObjectProperty");
    public static PropertyTypeName ArrayOf(PropertyTypeName element) => new("ArrayProperty", element);
}

/// <summary>An ordered list of tagged properties, terminated on disk by a "None" name.</summary>
public sealed class PropertyList : List<Property>
{
    public Property? Find(string name, int arrayIndex = 0) =>
        this.FirstOrDefault(p => p.Name == name && p.Tag.ArrayIndex == arrayIndex);

    public T? Get<T>(string name, int arrayIndex = 0) where T : PropertyValue =>
        Find(name, arrayIndex)?.Value as T;

    /// <summary>Returns the named property's value, adding the property (via <paramref name="create"/>) if absent.</summary>
    public T GetOrAdd<T>(string name, PropertyTypeName type, Func<T> create) where T : PropertyValue
    {
        if (Find(name) is { } existing)
            return existing.Value as T
                   ?? throw new InvalidOperationException($"Property '{name}' is {existing.Value.GetType().Name}, expected {typeof(T).Name}.");
        var value = create();
        Add(Property.Create(name, type, value));
        return value;
    }
}
