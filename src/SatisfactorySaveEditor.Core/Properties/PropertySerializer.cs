using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>
/// Reads and writes tagged property lists and values.
/// Every top-level property value is verified after parsing by re-serializing it and comparing with the
/// original bytes; values that cannot be parsed or reproduced exactly are kept as <see cref="RawValue"/>.
/// </summary>
public static class PropertySerializer
{
    public static PropertyList ReadList(SaveReader r, bool legacy)
    {
        var list = new PropertyList();
        while (PropertyTag.Read(r, legacy) is { } tag)
            list.Add(new Property(tag, ReadTopLevelValue(r, tag, legacy)));
        return list;
    }

    public static void WriteList(SaveWriter w, PropertyList list, bool legacy)
    {
        foreach (var property in list)
            WriteProperty(w, property, legacy);
        w.WriteFString("None");
    }

    public static void WriteProperty(SaveWriter w, Property property, bool legacy)
    {
        if (property.Type.Name == "BoolProperty" && property.Value is BoolValue b)
        {
            property.Tag.BoolValue = b.Value;
            int boolSizeAt = property.Tag.Write(w, legacy);
            w.PatchInt32(boolSizeAt, 0);
            return;
        }
        int sizeAt = property.Tag.Write(w, legacy);
        int start = w.Position;
        WriteValue(w, property.Value, legacy);
        w.PatchInt32(sizeAt, w.Position - start);
    }

    private static PropertyValue ReadTopLevelValue(SaveReader r, PropertyTag tag, bool legacy)
    {
        var slice = r.Slice(tag.Size);
        var original = slice.PeekBytes(tag.Size);

        if (tag.Type.Name == "BoolProperty" && tag.Size == 0)
            return new BoolValue(tag.BoolValue);

        try
        {
            var value = ReadValue(slice, tag.Type, legacy, tag.Size, tag);
            if (!slice.AtEnd)
                throw new SaveFormatException($"{slice.Remaining} unread bytes.");
            var check = new SaveWriter(tag.Size + 16);
            WriteValue(check, value, legacy);
            if (!check.WrittenSpan.SequenceEqual(original))
                throw new SaveFormatException("Re-serialized bytes differ from the original.");
            return value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new RawValue(original.ToArray()) { Reason = $"{tag.Type}: {ex.Message}" };
        }
    }

    /// <summary>Reads a value of <paramref name="type"/>. <paramref name="size"/> and <paramref name="tag"/> are null for container elements.</summary>
    public static PropertyValue ReadValue(SaveReader r, PropertyTypeName type, bool legacy, int? size, PropertyTag? tag)
    {
        switch (type.Name)
        {
            case "IntProperty": return new IntValue(r.ReadInt32());
            case "Int8Property": return new Int8Value(r.ReadSByte());
            case "Int16Property": return new Int16Value(r.ReadInt16());
            case "UInt16Property": return new UInt16Value(r.ReadUInt16());
            case "UInt32Property": return new UInt32Value(r.ReadUInt32());
            case "Int64Property": return new Int64Value(r.ReadInt64());
            case "UInt64Property": return new UInt64Value(r.ReadUInt64());
            case "FloatProperty": return new FloatValue(r.ReadSingle());
            case "DoubleProperty": return new DoubleValue(r.ReadDouble());
            case "BoolProperty": return new BoolValue(r.ReadByte() != 0);
            case "ByteProperty":
                // Enum-typed bytes are stored as the enumerator name; plain bytes as one byte.
                return size is > 1 ? new StringValue(r.ReadFString()) : new ByteValue(r.ReadByte());
            case "EnumProperty":
            case "NameProperty":
            case "StrProperty":
                return new StringValue(r.ReadFString());
            case "ObjectProperty":
            case "InterfaceProperty":
            case "WeakObjectProperty":
                return new ObjectValue(ObjectReference.Read(r));
            case "SoftObjectProperty":
            case "SoftClassProperty":
                return new SoftObjectValue(ObjectReference.Read(r), r.ReadFString());
            case "TextProperty":
                return ReadText(r, size);
            case "StructProperty":
                return ReadStruct(r, type.Param(0)?.Name, legacy, size, legacy ? null : tag?.IsNative);
            case "ArrayProperty":
                return ReadArray(r, type, legacy, size);
            case "SetProperty":
                return ReadSet(r, type, legacy);
            case "MapProperty":
                return ReadMap(r, type, legacy);
            default:
                throw new SaveFormatException($"Unsupported property type '{type}'.");
        }
    }

    public static void WriteValue(SaveWriter w, PropertyValue value, bool legacy)
    {
        switch (value)
        {
            case IntValue v: w.WriteInt32(v.Value); break;
            case Int8Value v: w.WriteSByte(v.Value); break;
            case Int16Value v: w.WriteInt16(v.Value); break;
            case UInt16Value v: w.WriteUInt16(v.Value); break;
            case UInt32Value v: w.WriteUInt32(v.Value); break;
            case Int64Value v: w.WriteInt64(v.Value); break;
            case UInt64Value v: w.WriteUInt64(v.Value); break;
            case FloatValue v: w.WriteSingle(v.Value); break;
            case DoubleValue v: w.WriteDouble(v.Value); break;
            case BoolValue v: w.WriteByte(v.Value ? (byte)1 : (byte)0); break;
            case ByteValue v: w.WriteByte(v.Value); break;
            case StringValue v: w.WriteFString(v.Value); break;
            case ObjectValue v: v.Value.Write(w); break;
            case SoftObjectValue v: v.Value.Write(w); w.WriteFString(v.SubPath); break;
            case TextValue v: WriteText(w, v); break;
            case PropertyListStruct v: WriteList(w, v.Properties, legacy); break;
            case NativeStruct v: v.Write(w); break;
            case InventoryItemStruct v: v.Write(w); break;
            case ClientIdentityInfoStruct v: v.Write(w); break;
            case UniqueNetIdReplStruct v: v.Write(w); break;
            case RawStruct v: w.WriteBytes(v.Bytes); break;
            case ArrayValue v: WriteArray(w, v, legacy); break;
            case SetValue v: WriteSet(w, v, legacy); break;
            case MapValue v: WriteMap(w, v, legacy); break;
            case RawValue v: w.WriteBytes(v.Bytes); break;
            default: throw new InvalidOperationException($"Cannot serialize {value.GetType().Name}.");
        }
    }

    /// <param name="nativeHint">Modern top-level tags say whether the struct is natively serialized; null when unknown.</param>
    private static StructValue ReadStruct(SaveReader r, string? structName, bool legacy, int? size, bool? nativeHint)
    {
        if (nativeHint != false)
        {
            switch (structName)
            {
                case "InventoryItem":
                    var item = new InventoryItemStruct();
                    item.Read(r, legacy);
                    return item;
                case "ClientIdentityInfo":
                    var identity = new ClientIdentityInfoStruct();
                    identity.Read(r);
                    return identity;
                case "UniqueNetIdRepl":
                    var netId = new UniqueNetIdReplStruct();
                    netId.Read(r);
                    return netId;
            }
        }

        var layout = NativeStructLayouts.Find(structName, size);
        if (nativeHint == true || (nativeHint is null && layout is not null))
        {
            if (layout is not null)
            {
                var native = new NativeStruct(layout);
                native.Read(r);
                return native;
            }
            if (size is null)
                throw new SaveFormatException($"Unknown native struct '{structName}' in a container.");
            return new RawStruct(structName, r.ReadBytes(size.Value));
        }

        return new PropertyListStruct(structName, ReadList(r, legacy));
    }

    private static ArrayValue ReadArray(SaveReader r, PropertyTypeName type, bool legacy, int? size)
    {
        var elementType = type.Param(0) ?? throw new SaveFormatException("ArrayProperty without element type.");
        int count = r.ReadInt32();
        var array = new ArrayValue(elementType);

        if (elementType.Name == "StructProperty")
        {
            string? structName;
            int? elementSize = null;
            if (legacy)
            {
                var inner = PropertyTag.Read(r, legacy: true)
                            ?? throw new SaveFormatException("Missing inner tag in legacy struct array.");
                array.LegacyInnerTag = inner;
                structName = inner.Type.Param(0)?.Name;
                if (count > 0 && inner.Size % count == 0)
                    elementSize = inner.Size / count;
            }
            else
            {
                structName = elementType.Param(0)?.Name;
                if (count > 0 && size is { } total && (total - 4) % count == 0)
                    elementSize = (total - 4) / count;
            }
            for (int i = 0; i < count; i++)
                array.Elements.Add(ReadStruct(r, structName, legacy, elementSize, null));
            return array;
        }

        for (int i = 0; i < count; i++)
            array.Elements.Add(ReadValue(r, elementType, legacy, null, null));
        return array;
    }

    private static void WriteArray(SaveWriter w, ArrayValue array, bool legacy)
    {
        w.WriteInt32(array.Elements.Count);
        if (array.LegacyInnerTag is { } inner)
        {
            int sizeAt = inner.Write(w, legacy: true);
            int start = w.Position;
            foreach (var element in array.Elements)
                WriteValue(w, element, legacy);
            w.PatchInt32(sizeAt, w.Position - start);
            return;
        }
        foreach (var element in array.Elements)
            WriteValue(w, element, legacy);
    }

    private static SetValue ReadSet(SaveReader r, PropertyTypeName type, bool legacy)
    {
        var elementType = type.Param(0) ?? throw new SaveFormatException("SetProperty without element type.");
        var set = new SetValue(elementType);
        int removed = r.ReadInt32();
        for (int i = 0; i < removed; i++)
            set.Removed.Add(ReadValue(r, elementType, legacy, null, null));
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            set.Elements.Add(ReadValue(r, elementType, legacy, null, null));
        return set;
    }

    private static void WriteSet(SaveWriter w, SetValue set, bool legacy)
    {
        w.WriteInt32(set.Removed.Count);
        foreach (var element in set.Removed)
            WriteValue(w, element, legacy);
        w.WriteInt32(set.Elements.Count);
        foreach (var element in set.Elements)
            WriteValue(w, element, legacy);
    }

    private static MapValue ReadMap(SaveReader r, PropertyTypeName type, bool legacy)
    {
        var keyType = type.Param(0) ?? throw new SaveFormatException("MapProperty without key type.");
        var valueType = type.Param(1) ?? throw new SaveFormatException("MapProperty without value type.");
        var map = new MapValue(keyType, valueType);
        int removed = r.ReadInt32();
        for (int i = 0; i < removed; i++)
            map.RemovedKeys.Add(ReadValue(r, keyType, legacy, null, null));
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var key = ReadValue(r, keyType, legacy, null, null);
            var value = ReadValue(r, valueType, legacy, null, null);
            map.Entries.Add(new(key, value));
        }
        return map;
    }

    private static void WriteMap(SaveWriter w, MapValue map, bool legacy)
    {
        w.WriteInt32(map.RemovedKeys.Count);
        foreach (var key in map.RemovedKeys)
            WriteValue(w, key, legacy);
        w.WriteInt32(map.Entries.Count);
        foreach (var (key, value) in map.Entries)
        {
            WriteValue(w, key, legacy);
            WriteValue(w, value, legacy);
        }
    }

    private static TextValue ReadText(SaveReader r, int? size)
    {
        var text = new TextValue { Flags = r.ReadInt32(), HistoryType = r.ReadSByte() };
        switch (text.HistoryType)
        {
            case TextValue.HistoryNone:
                text.HasCultureInvariantString = r.ReadInt32() != 0;
                if (text.HasCultureInvariantString)
                    text.Text = r.ReadFString();
                break;
            case TextValue.HistoryBase:
                text.Namespace = r.ReadFString();
                text.Key = r.ReadFString();
                text.Text = r.ReadFString();
                break;
            case TextValue.HistoryStringTableEntry:
                text.Namespace = r.ReadFString();
                text.Key = r.ReadFString();
                break;
            default:
                if (size is null)
                    throw new SaveFormatException($"Unsupported text history type {text.HistoryType} in a container.");
                text.RawHistory = r.ReadBytes(size.Value - 5);
                break;
        }
        return text;
    }

    private static void WriteText(SaveWriter w, TextValue text)
    {
        w.WriteInt32(text.Flags);
        w.WriteSByte(text.HistoryType);
        switch (text.HistoryType)
        {
            case TextValue.HistoryNone:
                w.WriteInt32(text.HasCultureInvariantString ? 1 : 0);
                if (text.HasCultureInvariantString)
                    w.WriteFString(text.Text);
                break;
            case TextValue.HistoryBase:
                w.WriteFString(text.Namespace);
                w.WriteFString(text.Key);
                w.WriteFString(text.Text);
                break;
            case TextValue.HistoryStringTableEntry:
                w.WriteFString(text.Namespace);
                w.WriteFString(text.Key);
                break;
            default:
                w.WriteBytes(text.RawHistory ?? []);
                break;
        }
    }
}
