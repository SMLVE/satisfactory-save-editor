using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>Uncompressed save header (header version 14, Satisfactory 1.1+).</summary>
public sealed class SaveHeader
{
    public const int SupportedHeaderVersion = 14;

    public uint HeaderVersion { get; set; }
    public uint SaveVersion { get; set; }
    public uint BuildVersion { get; set; }
    public string? SaveName { get; set; }
    public string? MapName { get; set; }
    public string? MapOptions { get; set; }
    public string? SessionName { get; set; }
    public uint PlayedSeconds { get; set; }
    public long SaveTimestampTicks { get; set; }
    public byte SessionVisibility { get; set; }
    public uint EditorObjectVersion { get; set; }
    public string? ModMetadata { get; set; }
    public uint ModFlags { get; set; }
    public string? SaveIdentifier { get; set; }
    public uint IsPartitionedWorld { get; set; }
    public uint Md5Valid { get; set; }
    public byte[] Md5Hash { get; set; } = new byte[16];
    public uint IsCreativeModeEnabled { get; set; }

    public DateTime SaveTimestamp => new(SaveTimestampTicks, DateTimeKind.Utc);
    public TimeSpan PlayTime => TimeSpan.FromSeconds(PlayedSeconds);

    public static SaveHeader Read(SaveReader r)
    {
        var h = new SaveHeader { HeaderVersion = r.ReadUInt32() };
        if (h.HeaderVersion != SupportedHeaderVersion)
            throw new SaveFormatException(
                $"Unsupported save header version {h.HeaderVersion}; only version {SupportedHeaderVersion} (Satisfactory 1.1+) is supported.");

        h.SaveVersion = r.ReadUInt32();
        h.BuildVersion = r.ReadUInt32();
        h.SaveName = r.ReadFString();
        h.MapName = r.ReadFString();
        h.MapOptions = r.ReadFString();
        h.SessionName = r.ReadFString();
        h.PlayedSeconds = r.ReadUInt32();
        h.SaveTimestampTicks = r.ReadInt64();
        h.SessionVisibility = r.ReadByte();
        h.EditorObjectVersion = r.ReadUInt32();
        h.ModMetadata = r.ReadFString();
        h.ModFlags = r.ReadUInt32();
        h.SaveIdentifier = r.ReadFString();
        h.IsPartitionedWorld = r.ReadUInt32();
        h.Md5Valid = r.ReadUInt32();
        h.Md5Hash = r.ReadBytes(16);
        h.IsCreativeModeEnabled = r.ReadUInt32();
        return h;
    }

    public void Write(SaveWriter w)
    {
        w.WriteUInt32(HeaderVersion);
        w.WriteUInt32(SaveVersion);
        w.WriteUInt32(BuildVersion);
        w.WriteFString(SaveName);
        w.WriteFString(MapName);
        w.WriteFString(MapOptions);
        w.WriteFString(SessionName);
        w.WriteUInt32(PlayedSeconds);
        w.WriteInt64(SaveTimestampTicks);
        w.WriteByte(SessionVisibility);
        w.WriteUInt32(EditorObjectVersion);
        w.WriteFString(ModMetadata);
        w.WriteUInt32(ModFlags);
        w.WriteFString(SaveIdentifier);
        w.WriteUInt32(IsPartitionedWorld);
        w.WriteUInt32(Md5Valid);
        w.WriteBytes(Md5Hash);
        w.WriteUInt32(IsCreativeModeEnabled);
    }
}
