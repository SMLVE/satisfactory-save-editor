using SatisfactorySaveEditor.Core.Compression;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Tests;

/// <summary>Locates and caches the sample saves in the repository's assets folder.</summary>
internal static class SampleSaves
{
    public static IEnumerable<object[]> All => [["sample.sav"], ["sample2.sav"], ["sample3.sav"]];

    private static readonly Dictionary<string, byte[]> Cache = [];

    public static string PathOf(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SatisfactorySaveEditor.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName, "assets", name);
    }

    public static byte[] Bytes(string name)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var bytes))
                Cache[name] = bytes = File.ReadAllBytes(PathOf(name));
            return bytes;
        }
    }

    /// <summary>The decompressed body exactly as stored in the file.</summary>
    public static byte[] OriginalBody(string name)
    {
        var reader = new SaveReader(Bytes(name));
        SaveHeader.Read(reader);
        return ChunkedZlib.Decompress(reader, out _);
    }
}
