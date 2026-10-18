using System.Globalization;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>A property value or container element. Serialization lives in <see cref="PropertySerializer"/>.</summary>
public abstract class PropertyValue;

public sealed class IntValue(int value) : PropertyValue { public int Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class Int8Value(sbyte value) : PropertyValue { public sbyte Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class Int16Value(short value) : PropertyValue { public short Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class UInt16Value(ushort value) : PropertyValue { public ushort Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class UInt32Value(uint value) : PropertyValue { public uint Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class Int64Value(long value) : PropertyValue { public long Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class UInt64Value(ulong value) : PropertyValue { public ulong Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class FloatValue(float value) : PropertyValue { public float Value { get; set; } = value; public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture); }
public sealed class DoubleValue(double value) : PropertyValue { public double Value { get; set; } = value; public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture); }
public sealed class BoolValue(bool value) : PropertyValue { public bool Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }
public sealed class ByteValue(byte value) : PropertyValue { public byte Value { get; set; } = value; public override string ToString() => Value.ToString(CultureInfo.InvariantCulture); }

/// <summary>Str, Name and Enum values (and enum-typed ByteProperty values), all stored as FStrings.</summary>
public sealed class StringValue(string? value) : PropertyValue
{
    public string? Value { get; set; } = value;
    public override string ToString() => Value ?? "(null)";
}

/// <summary>Object and interface references.</summary>
public sealed class ObjectValue(ObjectReference value) : PropertyValue
{
    public ObjectReference Value { get; set; } = value;
    public override string ToString() => Value.ToString();
}

public sealed class SoftObjectValue(ObjectReference value, string? subPath) : PropertyValue
{
    public ObjectReference Value { get; set; } = value;
    public string? SubPath { get; set; } = subPath;
    public override string ToString() => SubPath is null ? Value.ToString() : $"{Value}:{SubPath}";
}

/// <summary>FText. Common history types are parsed; others are kept as raw bytes.</summary>
public sealed class TextValue : PropertyValue
{
    public const sbyte HistoryNone = -1;
    public const sbyte HistoryBase = 0;
    public const sbyte HistoryStringTableEntry = 11;

    public int Flags { get; set; }
    public sbyte HistoryType { get; set; }

    /// <summary>History None: whether a culture-invariant string follows.</summary>
    public bool HasCultureInvariantString { get; set; }
    /// <summary>History None: culture-invariant string. History Base: source string.</summary>
    public string? Text { get; set; }
    /// <summary>History Base: localization namespace. History StringTableEntry: table id.</summary>
    public string? Namespace { get; set; }
    /// <summary>History Base / StringTableEntry: key.</summary>
    public string? Key { get; set; }
    /// <summary>Raw history payload for history types that are not parsed.</summary>
    public byte[]? RawHistory { get; set; }

    public override string ToString() => HistoryType switch
    {
        HistoryNone => Text ?? "",
        HistoryBase => Text ?? "",
        HistoryStringTableEntry => $"[{Namespace}] {Key}",
        _ => $"(text history {HistoryType})",
    };
}

/// <summary>Array of elements of one type.</summary>
public sealed class ArrayValue(PropertyTypeName elementType) : PropertyValue
{
    public PropertyTypeName ElementType { get; set; } = elementType;
    public List<PropertyValue> Elements { get; } = [];
    /// <summary>Legacy only: inner tag stored before struct elements.</summary>
    public PropertyTag? LegacyInnerTag { get; set; }
    public override string ToString() => $"{ElementType}[{Elements.Count}]";
}

public sealed class SetValue(PropertyTypeName elementType) : PropertyValue
{
    public PropertyTypeName ElementType { get; set; } = elementType;
    public List<PropertyValue> Removed { get; } = [];
    public List<PropertyValue> Elements { get; } = [];
    public override string ToString() => $"Set<{ElementType}>[{Elements.Count}]";
}

public sealed class MapValue(PropertyTypeName keyType, PropertyTypeName valueType) : PropertyValue
{
    public PropertyTypeName KeyType { get; set; } = keyType;
    public PropertyTypeName ValueType { get; set; } = valueType;
    public List<PropertyValue> RemovedKeys { get; } = [];
    public List<KeyValuePair<PropertyValue, PropertyValue>> Entries { get; } = [];
    public override string ToString() => $"Map<{KeyType}, {ValueType}>[{Entries.Count}]";
}

/// <summary>Bytes whose structure is unknown, kept verbatim.</summary>
public sealed class RawValue(byte[] bytes) : PropertyValue
{
    public byte[] Bytes { get; set; } = bytes;
    /// <summary>Why the value could not be parsed (diagnostics only).</summary>
    public string? Reason { get; init; }
    public override string ToString() => $"(raw, {Bytes.Length} bytes)";
}
