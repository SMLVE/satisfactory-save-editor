using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Properties;

public abstract class StructValue(string? structName) : PropertyValue
{
    public string? StructName { get; set; } = structName;
}

/// <summary>A struct serialized as a nested tagged property list.</summary>
public sealed class PropertyListStruct(string? structName, PropertyList properties) : StructValue(structName)
{
    public PropertyList Properties { get; } = properties;
    public override string ToString() => $"{StructName} {{{Properties.Count} properties}}";
}

/// <summary>A natively serialized struct whose bytes are not understood, kept verbatim.</summary>
public sealed class RawStruct(string? structName, byte[] bytes) : StructValue(structName)
{
    public byte[] Bytes { get; set; } = bytes;
    public override string ToString() => $"{StructName} (raw, {Bytes.Length} bytes)";
}

public enum NativeFieldKind : byte { Byte, Int32, UInt32, Int64, Float, Double, Guid }

public sealed record NativeField(string Name, NativeFieldKind Kind);

/// <summary>Layout of a fixed-size native struct.</summary>
public sealed record NativeStructLayout(string StructName, NativeField[] Fields)
{
    public int Size => Fields.Sum(f => f.Kind switch
    {
        NativeFieldKind.Byte => 1,
        NativeFieldKind.Int32 or NativeFieldKind.UInt32 or NativeFieldKind.Float => 4,
        NativeFieldKind.Int64 or NativeFieldKind.Double => 8,
        NativeFieldKind.Guid => 16,
        _ => throw new ArgumentOutOfRangeException(),
    });
}

/// <summary>A fixed-layout native struct (Vector, Quat, LinearColor, ...), edited field by field.</summary>
public sealed class NativeStruct(NativeStructLayout layout) : StructValue(layout.StructName)
{
    public NativeStructLayout Layout { get; } = layout;
    /// <summary>Field values boxed by kind: byte, int, uint, long, float, double or Guid.</summary>
    public object[] Values { get; } = new object[layout.Fields.Length];

    public object this[string field]
    {
        get => Values[IndexOf(field)];
        set => Values[IndexOf(field)] = value;
    }

    private int IndexOf(string field)
    {
        int i = Array.FindIndex(Layout.Fields, f => f.Name == field);
        return i >= 0 ? i : throw new KeyNotFoundException($"{StructName} has no field '{field}'.");
    }

    internal void Read(SaveReader r)
    {
        for (int i = 0; i < Layout.Fields.Length; i++)
        {
            Values[i] = Layout.Fields[i].Kind switch
            {
                NativeFieldKind.Byte => r.ReadByte(),
                NativeFieldKind.Int32 => r.ReadInt32(),
                NativeFieldKind.UInt32 => r.ReadUInt32(),
                NativeFieldKind.Int64 => r.ReadInt64(),
                NativeFieldKind.Float => r.ReadSingle(),
                NativeFieldKind.Double => r.ReadDouble(),
                NativeFieldKind.Guid => r.ReadGuid(),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    internal void Write(SaveWriter w)
    {
        for (int i = 0; i < Layout.Fields.Length; i++)
        {
            switch (Layout.Fields[i].Kind)
            {
                case NativeFieldKind.Byte: w.WriteByte((byte)Values[i]); break;
                case NativeFieldKind.Int32: w.WriteInt32((int)Values[i]); break;
                case NativeFieldKind.UInt32: w.WriteUInt32((uint)Values[i]); break;
                case NativeFieldKind.Int64: w.WriteInt64((long)Values[i]); break;
                case NativeFieldKind.Float: w.WriteSingle((float)Values[i]); break;
                case NativeFieldKind.Double: w.WriteDouble((double)Values[i]); break;
                case NativeFieldKind.Guid: w.WriteGuid((Guid)Values[i]); break;
            }
        }
    }

    public override string ToString() =>
        $"{StructName}({string.Join(", ", Layout.Fields.Select((f, i) => $"{f.Name}={Values[i]}"))})";
}

/// <summary>
/// An inventory item: the item descriptor class plus optional item state.
/// Modern layout: int32, FString item class, int32 has-state, [int32, FString state type, int32 size, state bytes].
/// Legacy layout: int32, FString item class, ObjectReference item state.
/// </summary>
public sealed class InventoryItemStruct() : StructValue("InventoryItem")
{
    public bool Legacy { get; set; }
    public int Padding { get; set; }
    public string? ItemClass { get; set; }

    // Modern
    public int HasState { get; set; }
    public int StatePadding { get; set; }
    public string? StateType { get; set; }
    /// <summary>Item state properties (e.g. EnergyStored), or null when the state could not be parsed.</summary>
    public PropertyList? State { get; set; }
    /// <summary>Raw state bytes, used only when <see cref="State"/> is null.</summary>
    public byte[] StateData { get; set; } = [];

    // Legacy
    public ObjectReference LegacyState { get; set; } = ObjectReference.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(ItemClass);

    internal void Read(SaveReader r, bool legacy)
    {
        Legacy = legacy;
        Padding = r.ReadInt32();
        ItemClass = r.ReadFString();
        if (legacy)
        {
            LegacyState = ObjectReference.Read(r);
            return;
        }
        HasState = r.ReadInt32();
        if (HasState != 0)
        {
            StatePadding = r.ReadInt32();
            StateType = r.ReadFString();
            int size = r.ReadInt32();
            StateData = r.ReadBytes(size);
            State = TryParseState(StateData);
            if (State is not null)
                StateData = [];
        }
    }

    private static PropertyList? TryParseState(byte[] bytes)
    {
        try
        {
            var reader = new SaveReader(bytes);
            var list = PropertySerializer.ReadList(reader, legacy: false);
            if (!reader.AtEnd)
                return null;
            var check = new SaveWriter(bytes.Length + 16);
            PropertySerializer.WriteList(check, list, legacy: false);
            return check.WrittenSpan.SequenceEqual(bytes) ? list : null;
        }
        catch (SaveFormatException)
        {
            return null;
        }
    }

    internal void Write(SaveWriter w)
    {
        w.WriteInt32(Padding);
        w.WriteFString(ItemClass);
        if (Legacy)
        {
            LegacyState.Write(w);
            return;
        }
        w.WriteInt32(HasState);
        if (HasState != 0)
        {
            w.WriteInt32(StatePadding);
            w.WriteFString(StateType);
            if (State is null)
            {
                w.WriteInt32(StateData.Length);
                w.WriteBytes(StateData);
                return;
            }
            int sizeAt = w.ReserveInt32();
            int start = w.Position;
            PropertySerializer.WriteList(w, State, legacy: false);
            w.PatchInt32(sizeAt, w.Position - start);
        }
    }

    public override string ToString() => IsEmpty ? "(empty)" : ItemClass!;
}

/// <summary>An online platform identity: platform byte (1 = Epic, 6 = Steam) and opaque id bytes.</summary>
public sealed record PlatformIdentity(byte Platform, byte[] Data);

/// <summary>A player's client identity: UUID plus one identity per online platform.</summary>
public sealed class ClientIdentityInfoStruct() : StructValue("ClientIdentityInfo")
{
    public string? Uuid { get; set; }
    public List<PlatformIdentity> Identities { get; } = [];

    internal void Read(SaveReader r)
    {
        Uuid = r.ReadFString();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            byte platform = r.ReadByte();
            Identities.Add(new PlatformIdentity(platform, r.ReadBytes(r.ReadInt32())));
        }
    }

    internal void Write(SaveWriter w)
    {
        w.WriteFString(Uuid);
        w.WriteInt32(Identities.Count);
        foreach (var identity in Identities)
        {
            w.WriteByte(identity.Platform);
            w.WriteInt32(identity.Data.Length);
            w.WriteBytes(identity.Data);
        }
    }

    public override string ToString() => $"{Uuid} ({Identities.Count} identities)";
}

/// <summary>Replicated unique net id: format byte, then (for format 0xF1) a platform identity.</summary>
public sealed class UniqueNetIdReplStruct() : StructValue("UniqueNetIdRepl")
{
    public const byte FormatWithIdentity = 0xF1;

    public byte Format { get; set; }
    public PlatformIdentity? Identity { get; set; }

    internal void Read(SaveReader r)
    {
        Format = r.ReadByte();
        if (Format == FormatWithIdentity)
        {
            byte platform = r.ReadByte();
            Identity = new PlatformIdentity(platform, r.ReadBytes(r.ReadInt32()));
        }
        else if (Format != 0)
            throw new SaveFormatException($"Unsupported UniqueNetIdRepl format 0x{Format:X2}.");
    }

    internal void Write(SaveWriter w)
    {
        w.WriteByte(Format);
        if (Format == FormatWithIdentity && Identity is not null)
        {
            w.WriteByte(Identity.Platform);
            w.WriteInt32(Identity.Data.Length);
            w.WriteBytes(Identity.Data);
        }
    }

    public override string ToString() => Identity is null ? "(none)" : $"platform {Identity.Platform}";
}

/// <summary>Known fixed-size native struct layouts. Vectors exist in double (UE5 LWC) and float variants.</summary>
public static class NativeStructLayouts
{
    private static NativeField[] F(NativeFieldKind kind, params string[] names) =>
        names.Select(n => new NativeField(n, kind)).ToArray();

    private static readonly Dictionary<string, NativeStructLayout[]> Layouts = new()
    {
        ["Vector"] = [new("Vector", F(NativeFieldKind.Double, "X", "Y", "Z")), new("Vector", F(NativeFieldKind.Float, "X", "Y", "Z"))],
        ["Rotator"] = [new("Rotator", F(NativeFieldKind.Double, "Pitch", "Yaw", "Roll")), new("Rotator", F(NativeFieldKind.Float, "Pitch", "Yaw", "Roll"))],
        ["Vector2D"] = [new("Vector2D", F(NativeFieldKind.Double, "X", "Y")), new("Vector2D", F(NativeFieldKind.Float, "X", "Y"))],
        ["Vector4"] = [new("Vector4", F(NativeFieldKind.Double, "X", "Y", "Z", "W")), new("Vector4", F(NativeFieldKind.Float, "X", "Y", "Z", "W"))],
        ["Quat"] = [new("Quat", F(NativeFieldKind.Double, "X", "Y", "Z", "W")), new("Quat", F(NativeFieldKind.Float, "X", "Y", "Z", "W"))],
        ["Box"] =
        [
            new("Box", [.. F(NativeFieldKind.Double, "MinX", "MinY", "MinZ", "MaxX", "MaxY", "MaxZ"), new("IsValid", NativeFieldKind.Byte)]),
            new("Box", [.. F(NativeFieldKind.Float, "MinX", "MinY", "MinZ", "MaxX", "MaxY", "MaxZ"), new("IsValid", NativeFieldKind.Byte)]),
        ],
        ["LinearColor"] = [new("LinearColor", F(NativeFieldKind.Float, "R", "G", "B", "A"))],
        ["Color"] = [new("Color", F(NativeFieldKind.Byte, "B", "G", "R", "A"))],
        ["IntPoint"] = [new("IntPoint", F(NativeFieldKind.Int32, "X", "Y"))],
        ["IntVector"] = [new("IntVector", F(NativeFieldKind.Int32, "X", "Y", "Z"))],
        ["IntVector4"] = [new("IntVector4", F(NativeFieldKind.Int32, "X", "Y", "Z", "W"))],
        ["Guid"] = [new("Guid", [new("Value", NativeFieldKind.Guid)])],
        ["DateTime"] = [new("DateTime", [new("Ticks", NativeFieldKind.Int64)])],
        ["Timespan"] = [new("Timespan", [new("Ticks", NativeFieldKind.Int64)])],
        ["FrameNumber"] = [new("FrameNumber", [new("Value", NativeFieldKind.Int32)])],
        ["FluidBox"] = [new("FluidBox", [new("Value", NativeFieldKind.Float)])],
        ["PlayerInfoHandle"] = [new("PlayerInfoHandle", [new("Platform", NativeFieldKind.Byte), new("Index", NativeFieldKind.Int32)])],
    };

    /// <summary>Picks the layout matching <paramref name="size"/> when known, else the first (double precision) variant.</summary>
    public static NativeStructLayout? Find(string? structName, int? size)
    {
        if (structName is null || !Layouts.TryGetValue(structName, out var variants))
            return null;
        if (size is null)
            return variants[0];
        return variants.FirstOrDefault(v => v.Size == size);
    }
}
