using System.IO.Compression;
using SatisfactorySaveEditor.Core.Compression;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core;

/// <summary>A loaded Satisfactory save: uncompressed header plus the parsed, decompressed body.</summary>
public sealed class SaveFile
{
    public const uint SupportedSaveVersion = 60;

    public required SaveHeader Header { get; init; }
    public required SaveBody Body { get; init; }
    public required ChunkedZlib.ChunkFormat ChunkFormat { get; init; }

    public static SaveFile Load(string path) => Load(File.ReadAllBytes(path));

    public static SaveFile Load(byte[] fileBytes)
    {
        var reader = new SaveReader(fileBytes);
        var header = SaveHeader.Read(reader);
        if (header.SaveVersion < SupportedSaveVersion)
            throw new SaveFormatException(
                $"Save version {header.SaveVersion} is older than the supported version {SupportedSaveVersion}.");

        var bodyBytes = ChunkedZlib.Decompress(reader, out var format);
        var body = SaveBody.Read(new SaveReader(bodyBytes));
        return new SaveFile { Header = header, Body = body, ChunkFormat = format };
    }

    /// <summary>Serializes the decompressed body (including its leading size field).</summary>
    public byte[] SerializeBody()
    {
        var w = new SaveWriter(4 << 20);
        Body.Write(w);
        return w.ToArray();
    }

    public byte[] ToBytes(CompressionLevel level = CompressionLevel.Optimal)
    {
        var body = SerializeBody();
        var w = new SaveWriter(body.Length / 4 + 4096);
        Header.Write(w);
        ChunkedZlib.Compress(body, ChunkFormat, w, level);
        return w.ToArray();
    }

    public void Save(string path, CompressionLevel level = CompressionLevel.Optimal) =>
        File.WriteAllBytes(path, ToBytes(level));
}
