using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>A streaming sublevel or the persistent level, holding object headers, object data and collectables.</summary>
public sealed class Level
{
    /// <summary>Sublevel name; null for the persistent level (which has no name field).</summary>
    public string? Name { get; set; }
    public bool IsPersistent { get; init; }

    public List<SaveObject> Objects { get; } = [];
    public List<ObjectReference> Collectables { get; } = [];
    /// <summary>
    /// Whether the header section ends with a collectables list. Some sublevels in older saves omit it
    /// entirely; presence is detected from the section size and reproduced on write.
    /// </summary>
    public bool HasCollectablesList { get; set; } = true;
    /// <summary>Second collectables list stored after the objects; normally a duplicate of <see cref="Collectables"/>.</summary>
    public List<ObjectReference> Collectables2 { get; } = [];
    /// <summary>Persistent level only: level names listed in the header section (e.g. "Persistent_Level").</summary>
    public List<string?> ExtraLevelNames { get; } = [];
    /// <summary>Persistent level only: level names listed after the objects.</summary>
    public List<string?> ExtraLevelNames2 { get; } = [];
    /// <summary>Per-level save version (sublevels only).</summary>
    public uint LevelSaveVersion { get; set; }
    /// <summary>Per-level engine versioning block (sublevels, save version 60+); null when the preceding flag is 0.</summary>
    public SaveVersionInfo? VersionInfo { get; set; }

    public static Level Read(SaveReader r, bool persistent)
    {
        var level = new Level { IsPersistent = persistent };
        if (!persistent)
            level.Name = r.ReadFString();

        long headersSize = r.ReadInt64();
        int headersStart = r.Position;
        int headerCount = r.ReadInt32();
        for (int i = 0; i < headerCount; i++)
            level.Objects.Add(SaveObject.ReadHeader(r));
        if (persistent)
            ReadNames(r, level.ExtraLevelNames);
        level.HasCollectablesList = r.Position < headersStart + headersSize;
        if (level.HasCollectablesList)
            ReadReferences(r, level.Collectables);
        CheckSize("headers", level, headersStart, r.Position, headersSize);

        long objectsSize = r.ReadInt64();
        int objectsStart = r.Position;
        int objectCount = r.ReadInt32();
        if (objectCount != headerCount)
            throw new SaveFormatException($"Level '{level.Name}' has {headerCount} headers but {objectCount} objects.");
        foreach (var obj in level.Objects)
            obj.ReadData(r);
        CheckSize("objects", level, objectsStart, r.Position, objectsSize);

        if (persistent)
        {
            ReadNames(r, level.ExtraLevelNames2);
            ReadReferences(r, level.Collectables2);
            return level;
        }

        level.LevelSaveVersion = r.ReadUInt32();
        ReadReferences(r, level.Collectables2);
        uint hasVersionInfo = r.ReadUInt32();
        if (hasVersionInfo > 1)
            throw new SaveFormatException($"Level '{level.Name}' has unexpected version-info flag {hasVersionInfo} at offset {r.Position - 4}.");
        if (hasVersionInfo == 1)
            level.VersionInfo = SaveVersionInfo.Read(r);
        return level;
    }

    public void Write(SaveWriter w)
    {
        if (!IsPersistent)
            w.WriteFString(Name);

        int headersSizeAt = w.ReserveInt64();
        int headersStart = w.Position;
        w.WriteInt32(Objects.Count);
        foreach (var obj in Objects)
            obj.WriteHeader(w);
        if (IsPersistent)
            WriteNames(w, ExtraLevelNames);
        if (HasCollectablesList || Collectables.Count > 0)
            WriteReferences(w, Collectables);
        w.PatchInt64(headersSizeAt, w.Position - headersStart);

        int objectsSizeAt = w.ReserveInt64();
        int objectsStart = w.Position;
        w.WriteInt32(Objects.Count);
        foreach (var obj in Objects)
            obj.WriteData(w);
        w.PatchInt64(objectsSizeAt, w.Position - objectsStart);

        if (IsPersistent)
        {
            WriteNames(w, ExtraLevelNames2);
            WriteReferences(w, Collectables2);
            return;
        }

        w.WriteUInt32(LevelSaveVersion);
        WriteReferences(w, Collectables2);
        w.WriteUInt32(VersionInfo is null ? 0u : 1u);
        VersionInfo?.Write(w);
    }

    private static void ReadNames(SaveReader r, List<string?> into)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            into.Add(r.ReadFString());
    }

    private static void WriteNames(SaveWriter w, List<string?> names)
    {
        w.WriteInt32(names.Count);
        foreach (var name in names)
            w.WriteFString(name);
    }

    private static void ReadReferences(SaveReader r, List<ObjectReference> into)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            into.Add(ObjectReference.Read(r));
    }

    private static void WriteReferences(SaveWriter w, List<ObjectReference> refs)
    {
        w.WriteInt32(refs.Count);
        foreach (var reference in refs)
            reference.Write(w);
    }

    private static void CheckSize(string section, Level level, int start, int end, long expected)
    {
        if (end - start != expected)
            throw new SaveFormatException(
                $"Level '{level.Name ?? "(persistent)"}' {section} section is {end - start} bytes, header says {expected} (section starts at {start}).");
    }

    public override string ToString() => $"{Name ?? "(persistent)"} ({Objects.Count} objects)";
}
