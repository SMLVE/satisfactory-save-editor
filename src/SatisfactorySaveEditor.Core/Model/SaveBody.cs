using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>Decompressed save body: versioning info, partition grids, levels and trailing references.</summary>
public sealed class SaveBody
{
    public SaveVersionInfo VersionInfo { get; set; } = new();
    public List<LevelGrid> Grids { get; } = [];
    public List<Level> Levels { get; } = [];
    public List<ObjectReference> TrailingReferences { get; } = [];
    /// <summary>Any bytes after the known structure, kept verbatim.</summary>
    public byte[] Tail { get; set; } = [];

    public Level PersistentLevel => Levels[^1];

    public IEnumerable<SaveObject> AllObjects => Levels.SelectMany(l => l.Objects);

    public static SaveBody Read(SaveReader r)
    {
        long declaredSize = r.ReadInt64();
        if (declaredSize != r.Remaining)
            throw new SaveFormatException($"Body declares {declaredSize} bytes but {r.Remaining} are present.");

        var body = new SaveBody { VersionInfo = SaveVersionInfo.Read(r) };

        int gridCount = r.ReadInt32();
        for (int i = 0; i < gridCount; i++)
            body.Grids.Add(LevelGrid.Read(r));

        int sublevelCount = r.ReadInt32();
        for (int i = 0; i < sublevelCount; i++)
            body.Levels.Add(Level.Read(r, persistent: false));
        body.Levels.Add(Level.Read(r, persistent: true));

        int refCount = r.ReadInt32();
        for (int i = 0; i < refCount; i++)
            body.TrailingReferences.Add(ObjectReference.Read(r));

        body.Tail = r.ReadBytes(r.Remaining);
        return body;
    }

    public void Write(SaveWriter w)
    {
        int sizeAt = w.ReserveInt64();
        int start = w.Position;

        VersionInfo.Write(w);

        w.WriteInt32(Grids.Count);
        foreach (var grid in Grids)
            grid.Write(w);

        w.WriteInt32(Levels.Count - 1);
        foreach (var level in Levels)
            level.Write(w);

        w.WriteInt32(TrailingReferences.Count);
        foreach (var reference in TrailingReferences)
            reference.Write(w);

        w.WriteBytes(Tail);
        w.PatchInt64(sizeAt, w.Position - start);
    }
}
