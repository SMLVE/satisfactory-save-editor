namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>Deep copies of property values, tags and lists.</summary>
public static class PropertyCloner
{
    public static PropertyTypeName Clone(PropertyTypeName type) =>
        new(type.Name, [.. type.Parameters.Select(Clone)]);

    public static PropertyTag Clone(PropertyTag tag) => new()
    {
        Name = tag.Name,
        Type = Clone(tag.Type),
        ArrayIndex = tag.ArrayIndex,
        PropertyGuid = tag.PropertyGuid,
        Flags = tag.Flags,
        ExtensionFlags = tag.ExtensionFlags,
        LegacyStructGuid = tag.LegacyStructGuid,
        BoolValue = tag.BoolValue,
        Size = tag.Size,
    };

    public static Property Clone(Property property) => new(Clone(property.Tag), Clone(property.Value));

    public static PropertyList Clone(PropertyList list)
    {
        var copy = new PropertyList();
        copy.AddRange(list.Select(Clone));
        return copy;
    }

    public static T Clone<T>(T value) where T : PropertyValue => (T)CloneValue(value);

    private static PropertyValue CloneValue(PropertyValue value)
    {
        switch (value)
        {
            case IntValue v: return new IntValue(v.Value);
            case Int8Value v: return new Int8Value(v.Value);
            case Int16Value v: return new Int16Value(v.Value);
            case UInt16Value v: return new UInt16Value(v.Value);
            case UInt32Value v: return new UInt32Value(v.Value);
            case Int64Value v: return new Int64Value(v.Value);
            case UInt64Value v: return new UInt64Value(v.Value);
            case FloatValue v: return new FloatValue(v.Value);
            case DoubleValue v: return new DoubleValue(v.Value);
            case BoolValue v: return new BoolValue(v.Value);
            case ByteValue v: return new ByteValue(v.Value);
            case StringValue v: return new StringValue(v.Value);
            case ObjectValue v: return new ObjectValue(v.Value);
            case SoftObjectValue v: return new SoftObjectValue(v.Value, v.SubPath);
            case TextValue v:
                return new TextValue
                {
                    Flags = v.Flags, HistoryType = v.HistoryType, HasCultureInvariantString = v.HasCultureInvariantString,
                    Text = v.Text, Namespace = v.Namespace, Key = v.Key, RawHistory = v.RawHistory?.ToArray(),
                };
            case PropertyListStruct v: return new PropertyListStruct(v.StructName, Clone(v.Properties));
            case RawStruct v: return new RawStruct(v.StructName, v.Bytes.ToArray());
            case NativeStruct v:
            {
                var copy = new NativeStruct(v.Layout);
                Array.Copy(v.Values, copy.Values, v.Values.Length);
                return copy;
            }
            case InventoryItemStruct v:
                return new InventoryItemStruct
                {
                    Legacy = v.Legacy, Padding = v.Padding, ItemClass = v.ItemClass, HasState = v.HasState,
                    StatePadding = v.StatePadding, StateType = v.StateType, State = v.State is null ? null : Clone(v.State),
                    StateData = v.StateData.ToArray(), LegacyState = v.LegacyState,
                };
            case ClientIdentityInfoStruct v:
            {
                var copy = new ClientIdentityInfoStruct { Uuid = v.Uuid };
                copy.Identities.AddRange(v.Identities.Select(i => i with { Data = i.Data.ToArray() }));
                return copy;
            }
            case UniqueNetIdReplStruct v:
                return new UniqueNetIdReplStruct { Format = v.Format, Identity = v.Identity is null ? null : v.Identity with { Data = v.Identity.Data.ToArray() } };
            case ArrayValue v:
            {
                var copy = new ArrayValue(Clone(v.ElementType)) { LegacyInnerTag = v.LegacyInnerTag is null ? null : Clone(v.LegacyInnerTag) };
                copy.Elements.AddRange(v.Elements.Select(CloneValue));
                return copy;
            }
            case SetValue v:
            {
                var copy = new SetValue(Clone(v.ElementType));
                copy.Removed.AddRange(v.Removed.Select(CloneValue));
                copy.Elements.AddRange(v.Elements.Select(CloneValue));
                return copy;
            }
            case MapValue v:
            {
                var copy = new MapValue(Clone(v.KeyType), Clone(v.ValueType));
                copy.RemovedKeys.AddRange(v.RemovedKeys.Select(CloneValue));
                copy.Entries.AddRange(v.Entries.Select(e => new KeyValuePair<PropertyValue, PropertyValue>(CloneValue(e.Key), CloneValue(e.Value))));
                return copy;
            }
            case RawValue v: return new RawValue(v.Bytes.ToArray()) { Reason = v.Reason };
            default: throw new InvalidOperationException($"Cannot clone {value.GetType().Name}.");
        }
    }
}
