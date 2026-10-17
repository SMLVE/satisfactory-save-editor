using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>World-partition grid descriptor: a named grid with a cell size, hash and per-cell level hashes.</summary>
public sealed class LevelGrid
{
    public string? Name { get; set; }
    public uint CellSize { get; set; }
    public uint GridHash { get; set; }
    public List<(string? CellName, uint CellHash)> Cells { get; } = [];

    public static LevelGrid Read(SaveReader r)
    {
        var g = new LevelGrid { Name = r.ReadFString(), CellSize = r.ReadUInt32(), GridHash = r.ReadUInt32() };
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            g.Cells.Add((r.ReadFString(), r.ReadUInt32()));
        return g;
    }

    public void Write(SaveWriter w)
    {
        w.WriteFString(Name);
        w.WriteUInt32(CellSize);
        w.WriteUInt32(GridHash);
        w.WriteInt32(Cells.Count);
        foreach (var (name, hash) in Cells)
        {
            w.WriteFString(name);
            w.WriteUInt32(hash);
        }
    }

    public override string ToString() => $"{Name} ({Cells.Count} cells)";
}
