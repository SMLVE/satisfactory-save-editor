using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>
/// Engine/package versioning block at the start of the body (new since save version 46).
/// Contains UE file versions, the engine version and the custom version table.
/// </summary>
public sealed class SaveVersionInfo
{
    public int Unknown0 { get; set; }
    public int FileVersionUE4 { get; set; }
    public int FileVersionUE5 { get; set; }
    public int LicenseeVersion { get; set; }
    public ushort EngineMajor { get; set; }
    public ushort EngineMinor { get; set; }
    public ushort EnginePatch { get; set; }
    public uint EngineChangelist { get; set; }
    public string? EngineBranch { get; set; }
    public List<(Guid Key, int Version)> CustomVersions { get; } = [];

    public string EngineVersion => $"{EngineMajor}.{EngineMinor}.{EnginePatch}";

    public static SaveVersionInfo Read(SaveReader r)
    {
        var v = new SaveVersionInfo
        {
            Unknown0 = r.ReadInt32(),
            FileVersionUE4 = r.ReadInt32(),
            FileVersionUE5 = r.ReadInt32(),
            LicenseeVersion = r.ReadInt32(),
            EngineMajor = r.ReadUInt16(),
            EngineMinor = r.ReadUInt16(),
            EnginePatch = r.ReadUInt16(),
            EngineChangelist = r.ReadUInt32(),
            EngineBranch = r.ReadFString(),
        };
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            v.CustomVersions.Add((r.ReadGuid(), r.ReadInt32()));
        return v;
    }

    public void Write(SaveWriter w)
    {
        w.WriteInt32(Unknown0);
        w.WriteInt32(FileVersionUE4);
        w.WriteInt32(FileVersionUE5);
        w.WriteInt32(LicenseeVersion);
        w.WriteUInt16(EngineMajor);
        w.WriteUInt16(EngineMinor);
        w.WriteUInt16(EnginePatch);
        w.WriteUInt32(EngineChangelist);
        w.WriteFString(EngineBranch);
        w.WriteInt32(CustomVersions.Count);
        foreach (var (key, version) in CustomVersions)
        {
            w.WriteGuid(key);
            w.WriteInt32(version);
        }
    }
}
