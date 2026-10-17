using System.Numerics;
using SatisfactorySaveEditor.Core.IO;

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

    /// <summary>Object data as stored on disk (everything after the size field). Parsed lazily in later phases.</summary>
    public byte[] RawData { get; set; } = [];

    /// <summary>Uint32 stored after the sized object data (save version 60+); observed as 0.</summary>
    public uint PostDataValue { get; set; }

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
    }

    /// <summary>
    /// Objects written at save version 60 carry an extra uint32 after their sized data; objects kept from
    /// version 17 do not. Versions 18–59 have not been observed, so the exact cutoff is unknown.
    /// </summary>
    private const uint PostDataMinVersion = 60;
    private bool HasPostDataValue => ObjectSaveVersion >= PostDataMinVersion;

    internal void WriteData(SaveWriter w)
    {
        w.WriteUInt32(ObjectSaveVersion);
        w.WriteUInt32(ObjectFlag);
        w.WriteInt32(RawData.Length);
        w.WriteBytes(RawData);
        if (HasPostDataValue)
            w.WriteUInt32(PostDataValue);
    }

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
