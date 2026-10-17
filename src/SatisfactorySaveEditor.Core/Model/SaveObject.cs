using System.Numerics;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>
/// A saved UObject. The header (identity, transform) is stored in the level's header table and the
/// data (properties, trailing bytes) in the object table; this type joins both.
/// </summary>
public abstract class SaveObject
{
    public string? TypePath { get; set; }
    public string? RootObject { get; set; }
    public string? InstanceName { get; set; }
    public uint HeaderFlags { get; set; }

    /// <summary>Save version the object data was written with (may be older than the file).</summary>
    public uint ObjectSaveVersion { get; set; }
    public uint ObjectFlag { get; set; }

    /// <summary>
    /// Object data exactly as stored on disk, kept only when it could not be parsed (or is empty).
    /// When <see cref="IsParsed"/> is true the data is serialized from the parsed model instead.
    /// </summary>
    public byte[] RawData { get; set; } = [];

    /// <summary>Uint32 stored after the sized object data (save version 60+); observed as 0.</summary>
    public uint PostDataValue { get; set; }

    /// <summary>True when the object data was parsed into <see cref="Properties"/> and verified to round-trip.</summary>
    public bool IsParsed { get; private set; }
    /// <summary>Why parsing failed, when <see cref="IsParsed"/> is false and data was present.</summary>
    public string? ParseError { get; private set; }

    /// <summary>Objects older than save version 60 use legacy (UE4-style) property tags.</summary>
    public bool UsesLegacyTags => ObjectSaveVersion < ModernMinVersion;

    /// <summary>Modern only: serialization-control byte before the property list.</summary>
    public byte SerializationControl { get; set; }
    public PropertyList Properties { get; private set; } = [];
    /// <summary>Object GUID following the property list, if any.</summary>
    public Guid? ObjectGuid { get; set; }
    /// <summary>Class-specific bytes after the property list, kept verbatim.</summary>
    public byte[] TrailingData { get; set; } = [];

    public string ClassName
    {
        get
        {
            var path = TypePath ?? "";
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path[(dot + 1)..] : path;
        }
    }

    public ObjectReference Reference => new(RootObject, InstanceName);

    internal static SaveObject ReadHeader(SaveReader r)
    {
        uint kind = r.ReadUInt32();
        SaveObject obj = kind switch
        {
            1 => new ActorObject(),
            0 => new ComponentObject(),
            _ => throw new SaveFormatException($"Unknown object header type {kind} at offset {r.Position - 4}."),
        };
        obj.TypePath = r.ReadFString();
        obj.RootObject = r.ReadFString();
        obj.InstanceName = r.ReadFString();
        obj.HeaderFlags = r.ReadUInt32();
        obj.ReadHeaderTail(r);
        return obj;
    }

    internal void WriteHeader(SaveWriter w)
    {
        w.WriteUInt32(this is ActorObject ? 1u : 0u);
        w.WriteFString(TypePath);
        w.WriteFString(RootObject);
        w.WriteFString(InstanceName);
        w.WriteUInt32(HeaderFlags);
        WriteHeaderTail(w);
    }

    internal void ReadData(SaveReader r)
    {
        ObjectSaveVersion = r.ReadUInt32();
        ObjectFlag = r.ReadUInt32();
        int size = r.ReadInt32();
        RawData = r.ReadBytes(size);
        if (HasPostDataValue)
        {
            PostDataValue = r.ReadUInt32();
            if (PostDataValue != 0)
                throw new SaveFormatException(
                    $"Object '{InstanceName}' (save version {ObjectSaveVersion}) has non-zero post-data value {PostDataValue} at offset {r.Position - 4}.");
        }
        TryParseData();
    }

    /// <summary>
    /// Objects written at save version 60 carry an extra uint32 after their sized data and use modern
    /// property tags; objects kept from version 17 do neither. Versions 18–59 have not been observed,
    /// so the exact cutoff is unknown.
    /// </summary>
    private const uint ModernMinVersion = 60;
    private bool HasPostDataValue => ObjectSaveVersion >= ModernMinVersion;

    internal void WriteData(SaveWriter w)
    {
        w.WriteUInt32(ObjectSaveVersion);
        w.WriteUInt32(ObjectFlag);
        if (IsParsed)
        {
            int sizeAt = w.ReserveInt32();
            int start = w.Position;
            WriteParsedData(w);
            w.PatchInt32(sizeAt, w.Position - start);
        }
        else
        {
            w.WriteInt32(RawData.Length);
            w.WriteBytes(RawData);
        }
        if (HasPostDataValue)
            w.WriteUInt32(PostDataValue);
    }

    private void TryParseData()
    {
        if (RawData.Length == 0)
            return;
        try
        {
            var r = new SaveReader(RawData);
            ReadDataPrefix(r);
            bool legacy = UsesLegacyTags;
            if (!legacy)
            {
                SerializationControl = r.ReadByte();
                if (SerializationControl != 0)
                    throw new SaveFormatException($"Unsupported serialization control byte {SerializationControl}.");
            }
            Properties = PropertySerializer.ReadList(r, legacy);
            int hasGuid = r.ReadInt32();
            if (hasGuid is not (0 or 1))
                throw new SaveFormatException($"Unexpected object GUID flag {hasGuid}.");
            ObjectGuid = hasGuid == 1 ? r.ReadGuid() : null;
            TrailingData = r.ReadBytes(r.Remaining);

            var check = new SaveWriter(RawData.Length + 16);
            WriteParsedData(check);
            if (!check.WrittenSpan.SequenceEqual(RawData))
                throw new SaveFormatException("Re-serialized object data differs from the original.");

            IsParsed = true;
            RawData = [];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ParseError = ex.Message;
            Properties = [];
            TrailingData = [];
            ObjectGuid = null;
            ResetDataPrefix();
        }
    }

    private void WriteParsedData(SaveWriter w)
    {
        WriteDataPrefix(w);
        bool legacy = UsesLegacyTags;
        if (!legacy)
            w.WriteByte(SerializationControl);
        PropertySerializer.WriteList(w, Properties, legacy);
        w.WriteInt32(ObjectGuid.HasValue ? 1 : 0);
        if (ObjectGuid.HasValue)
            w.WriteGuid(ObjectGuid.Value);
        w.WriteBytes(TrailingData);
    }

    /// <summary>Data stored before the property list (actors: parent and component references).</summary>
    protected virtual void ReadDataPrefix(SaveReader r) { }
    protected virtual void WriteDataPrefix(SaveWriter w) { }
    protected virtual void ResetDataPrefix() { }

    protected abstract void ReadHeaderTail(SaveReader r);
    protected abstract void WriteHeaderTail(SaveWriter w);

    public override string ToString() => $"{ClassName} {InstanceName}";
}

public sealed class ActorObject : SaveObject
{
    public uint NeedTransform { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;
    public uint WasPlacedInLevel { get; set; }

    public ObjectReference Parent { get; set; } = ObjectReference.Empty;
    public List<ObjectReference> Components { get; } = [];

    protected override void ReadDataPrefix(SaveReader r)
    {
        Parent = ObjectReference.Read(r);
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            Components.Add(ObjectReference.Read(r));
    }

    protected override void WriteDataPrefix(SaveWriter w)
    {
        Parent.Write(w);
        w.WriteInt32(Components.Count);
        foreach (var component in Components)
            component.Write(w);
    }

    protected override void ResetDataPrefix()
    {
        Parent = ObjectReference.Empty;
        Components.Clear();
    }

    protected override void ReadHeaderTail(SaveReader r)
    {
        NeedTransform = r.ReadUInt32();
        Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Scale = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        WasPlacedInLevel = r.ReadUInt32();
    }

    protected override void WriteHeaderTail(SaveWriter w)
    {
        w.WriteUInt32(NeedTransform);
        w.WriteSingle(Rotation.X); w.WriteSingle(Rotation.Y); w.WriteSingle(Rotation.Z); w.WriteSingle(Rotation.W);
        w.WriteSingle(Position.X); w.WriteSingle(Position.Y); w.WriteSingle(Position.Z);
        w.WriteSingle(Scale.X); w.WriteSingle(Scale.Y); w.WriteSingle(Scale.Z);
        w.WriteUInt32(WasPlacedInLevel);
    }
}

public sealed class ComponentObject : SaveObject
{
    public string? ParentActorName { get; set; }

    protected override void ReadHeaderTail(SaveReader r) => ParentActorName = r.ReadFString();
    protected override void WriteHeaderTail(SaveWriter w) => w.WriteFString(ParentActorName);
}
