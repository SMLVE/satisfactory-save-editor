using System.IO.Compression;
using SatisfactorySaveEditor.Core.Compression;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Core;

/// <summary>A loaded Satisfactory save: uncompressed header plus the parsed, decompressed body.</summary>
public sealed class SaveFile
{
    public const uint SupportedSaveVersion = 60;

    public required SaveHeader Header { get; init; }
    public required SaveBody Body { get; init; }
    public required ChunkFormat ChunkFormat { get; init; }

    public static SaveFile Load(string path) => Load(File.ReadAllBytes(path));

    public static SaveFile Load(byte[] fileBytes)
    {
        var reader = new SaveReader(fileBytes);
        var header = SaveHeader.Read(reader);
        // Newer versions are refused too: their format may differ in ways that still parse but mean something else.
        if (header.SaveVersion != SupportedSaveVersion)
            throw new SaveFormatException(
                $"Save version {header.SaveVersion} is {(header.SaveVersion < SupportedSaveVersion ? "older" : "newer")} than the supported version {SupportedSaveVersion}.");

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

    public byte[] ToBytes(CompressionLevel level = CompressionLevel.Optimal) => Compose(SerializeBody(), level);

    /// <summary>
    /// Serializes the save and verifies the result before returning it: the bytes are loaded back, every
    /// object that was parsed in this save must parse again, and the reloaded body must re-serialize to the
    /// exact same bytes. Throws <see cref="SaveValidationException"/> when an edit produced an invalid save.
    /// </summary>
    public byte[] ToValidatedBytes(CompressionLevel level = CompressionLevel.Optimal)
    {
        var body = SerializeBody();
        var bytes = Compose(body, level);

        SaveFile reloaded;
        try
        {
            reloaded = Load(bytes);
        }
        catch (SaveFormatException ex)
        {
            throw new SaveValidationException($"The edited save could not be read back: {ex.Message}", ex);
        }

        // An object that no longer parses, or a value that was typed but now only reads as raw bytes,
        // means the edit wrote something the parser (and likely the game) cannot understand.
        var before = Body.AllObjects.Where(o => o.IsParsed)
            .GroupBy(o => o.InstanceName ?? "")
            .ToDictionary(g => g.Key, g => RawValueCount(g.First()));
        var broken = new List<string>();
        foreach (var obj in reloaded.Body.AllObjects)
        {
            if (!before.TryGetValue(obj.InstanceName ?? "", out int rawBefore))
                continue;
            if (!obj.IsParsed && obj.RawData.Length > 0)
                broken.Add($"{obj.InstanceName}: {obj.ParseError}");
            else if (RawValueCount(obj) > rawBefore)
                broken.Add($"{obj.InstanceName}: a property no longer reads back as its type");
        }
        if (broken.Count > 0)
            throw new SaveValidationException(
                $"{broken.Count} edited object(s) would no longer be readable:\n{string.Join("\n", broken.Take(5))}");

        if (!reloaded.SerializeBody().AsSpan().SequenceEqual(body))
            throw new SaveValidationException("The edited save does not read back to the same data.");
        return bytes;
    }

    public void Save(string path, CompressionLevel level = CompressionLevel.Optimal) =>
        File.WriteAllBytes(path, ToValidatedBytes(level));

    private static int RawValueCount(SaveObject obj) =>
        PropertyWalker.Descendants(obj.Properties).Count(v => v is RawValue or RawStruct);

    private byte[] Compose(byte[] body, CompressionLevel level)
    {
        var w = new SaveWriter(body.Length / 4 + 4096);
        Header.Write(w);
        ChunkedZlib.Compress(body, ChunkFormat, w, level);
        return w.ToArray();
    }
}
