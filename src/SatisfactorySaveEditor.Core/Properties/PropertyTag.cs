using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Properties;

/// <summary>Modern (UE 5.4+) property tag flags.</summary>
[Flags]
public enum PropertyTagFlags : byte
{
    None = 0,
    HasArrayIndex = 0x01,
    HasPropertyGuid = 0x02,
    HasPropertyExtensions = 0x04,
    HasBinaryOrNativeSerialize = 0x08,
    BoolTrue = 0x10,
    SkippedSerialize = 0x20,
}

/// <summary>
/// Header of a tagged property. Supports both the legacy UE4-style layout (used by objects kept from old
/// saves) and the modern UE 5.4+ layout with complete type names.
/// </summary>
public sealed class PropertyTag
{
    public required string? Name { get; set; }
    public required PropertyTypeName Type { get; set; }
    public int ArrayIndex { get; set; }
    public Guid? PropertyGuid { get; set; }

    /// <summary>Modern only: flags byte as stored (the BoolTrue bit is recomputed from the value on write).</summary>
    public PropertyTagFlags Flags { get; set; }
    /// <summary>Modern only: property extension flags (present when <see cref="PropertyTagFlags.HasPropertyExtensions"/> is set).</summary>
    public byte ExtensionFlags { get; set; }

    /// <summary>Legacy only: struct GUID stored after the struct name.</summary>
    public Guid LegacyStructGuid { get; set; }

    /// <summary>Bool properties store their value in the tag itself.</summary>
    public bool BoolValue { get; set; }

    /// <summary>Payload size as read from disk (informational; recomputed on write).</summary>
    public int Size { get; set; }

    public bool IsNative => Flags.HasFlag(PropertyTagFlags.HasBinaryOrNativeSerialize);

    /// <summary>Reads a tag, or returns null at the "None" terminator.</summary>
    public static PropertyTag? Read(SaveReader r, bool legacy)
    {
        var name = r.ReadFString();
        if (name == "None")
            return null;
        return legacy ? ReadLegacy(r, name) : ReadModern(r, name);
    }

    private static PropertyTag ReadModern(SaveReader r, string? name)
    {
        var tag = new PropertyTag { Name = name, Type = PropertyTypeName.Read(r) };
        tag.Size = r.ReadInt32();
        tag.Flags = (PropertyTagFlags)r.ReadByte();
        if ((tag.Flags & ~(PropertyTagFlags)0x3F) != 0)
            throw new SaveFormatException($"Unknown property tag flags 0x{(byte)tag.Flags:X2} on '{name}'.");
        if (tag.Flags.HasFlag(PropertyTagFlags.HasArrayIndex))
            tag.ArrayIndex = r.ReadInt32();
        if (tag.Flags.HasFlag(PropertyTagFlags.HasPropertyGuid))
            tag.PropertyGuid = r.ReadGuid();
        if (tag.Flags.HasFlag(PropertyTagFlags.HasPropertyExtensions))
        {
            tag.ExtensionFlags = r.ReadByte();
            if (tag.ExtensionFlags != 0)
                throw new SaveFormatException($"Unsupported property extension flags 0x{tag.ExtensionFlags:X2} on '{name}'.");
        }
        tag.BoolValue = tag.Flags.HasFlag(PropertyTagFlags.BoolTrue);
        return tag;
    }

    private static PropertyTag ReadLegacy(SaveReader r, string? name)
    {
        var typeName = r.ReadFString();
        var tag = new PropertyTag { Name = name, Type = new PropertyTypeName(typeName) };
        tag.Size = r.ReadInt32();
        tag.ArrayIndex = r.ReadInt32();
        switch (typeName)
        {
            case "StructProperty":
                tag.Type.Parameters.Add(new PropertyTypeName(r.ReadFString()));
                tag.LegacyStructGuid = r.ReadGuid();
                break;
            case "BoolProperty":
                tag.BoolValue = r.ReadByte() != 0;
                break;
            case "ByteProperty":
            case "EnumProperty":
            case "ArrayProperty":
            case "SetProperty":
                tag.Type.Parameters.Add(new PropertyTypeName(r.ReadFString()));
                break;
            case "MapProperty":
                tag.Type.Parameters.Add(new PropertyTypeName(r.ReadFString()));
                tag.Type.Parameters.Add(new PropertyTypeName(r.ReadFString()));
                break;
        }
        if (r.ReadByte() != 0)
            tag.PropertyGuid = r.ReadGuid();
        return tag;
    }

    /// <summary>Writes the tag and returns the offset of the size field, to be patched once the payload is written.</summary>
    public int Write(SaveWriter w, bool legacy)
    {
        w.WriteFString(Name);
        return legacy ? WriteLegacy(w) : WriteModern(w);
    }

    private int WriteModern(SaveWriter w)
    {
        Type.Write(w);
        int sizeAt = w.ReserveInt32();
        var flags = Flags & ~PropertyTagFlags.BoolTrue;
        if (Type.Name == "BoolProperty" && BoolValue)
            flags |= PropertyTagFlags.BoolTrue;
        w.WriteByte((byte)flags);
        if (flags.HasFlag(PropertyTagFlags.HasArrayIndex))
            w.WriteInt32(ArrayIndex);
        if (flags.HasFlag(PropertyTagFlags.HasPropertyGuid))
            w.WriteGuid(PropertyGuid ?? Guid.Empty);
        if (flags.HasFlag(PropertyTagFlags.HasPropertyExtensions))
            w.WriteByte(ExtensionFlags);
        return sizeAt;
    }

    private int WriteLegacy(SaveWriter w)
    {
        w.WriteFString(Type.Name);
        int sizeAt = w.ReserveInt32();
        w.WriteInt32(ArrayIndex);
        switch (Type.Name)
        {
            case "StructProperty":
                w.WriteFString(Type.Param(0)?.Name);
                w.WriteGuid(LegacyStructGuid);
                break;
            case "BoolProperty":
                w.WriteByte(BoolValue ? (byte)1 : (byte)0);
                break;
            case "ByteProperty":
            case "EnumProperty":
            case "ArrayProperty":
            case "SetProperty":
                w.WriteFString(Type.Param(0)?.Name);
                break;
            case "MapProperty":
                w.WriteFString(Type.Param(0)?.Name);
                w.WriteFString(Type.Param(1)?.Name);
                break;
        }
        w.WriteByte(PropertyGuid.HasValue ? (byte)1 : (byte)0);
        if (PropertyGuid.HasValue)
            w.WriteGuid(PropertyGuid.Value);
        return sizeAt;
    }

    public override string ToString() => $"{Name}: {Type}";
}
