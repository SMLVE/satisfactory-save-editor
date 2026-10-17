using System.Diagnostics;
using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Core.Compression;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: sse <info|decompress|roundtrip-check> <save.sav> [output]");
    return 1;
}

var path = args[1];

switch (args[0])
{
    case "info":
    {
        var sw = Stopwatch.StartNew();
        var save = SaveFile.Load(path);
        var h = save.Header;
        Console.WriteLine($"Loaded in      : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Header version : {h.HeaderVersion}");
        Console.WriteLine($"Save version   : {h.SaveVersion}");
        Console.WriteLine($"Build version  : {h.BuildVersion}");
        Console.WriteLine($"Engine         : {save.Body.VersionInfo.EngineVersion} ({save.Body.VersionInfo.EngineBranch})");
        Console.WriteLine($"Save name      : {h.SaveName}");
        Console.WriteLine($"Session name   : {h.SessionName}");
        Console.WriteLine($"Play time      : {h.PlayTime}");
        Console.WriteLine($"Saved at       : {h.SaveTimestamp:u}");
        Console.WriteLine($"Grids          : {string.Join(", ", save.Body.Grids)}");
        Console.WriteLine($"Levels         : {save.Body.Levels.Count}");
        Console.WriteLine($"Objects        : {save.Body.AllObjects.Count()}");
        Console.WriteLine($"Persistent     : {save.Body.PersistentLevel}");
        Console.WriteLine($"Trailing refs  : {save.Body.TrailingReferences.Count}");
        Console.WriteLine($"Unparsed tail  : {save.Body.Tail.Length} bytes");
        Console.WriteLine("Sublevels (save version, has collectables list, empty):");
        foreach (var g in save.Body.Levels.Where(l => !l.IsPersistent)
                     .GroupBy(l => (l.LevelSaveVersion, l.HasCollectablesList, Empty: l.Objects.Count == 0)).OrderBy(g => g.Key))
            Console.WriteLine($"  v{g.Key.LevelSaveVersion,-3} {g.Key.HasCollectablesList,-5} empty={g.Key.Empty,-5} {g.Count(),6}");
        Console.WriteLine("Object save versions:");
        foreach (var g in save.Body.AllObjects.GroupBy(o => (o.ObjectSaveVersion, o.ObjectFlag)).OrderBy(g => g.Key))
            Console.WriteLine($"  v{g.Key.ObjectSaveVersion,-3} flag {g.Key.ObjectFlag} {g.Count(),8}");
        return 0;
    }

    case "decompress":
    {
        var reader = new SaveReader(File.ReadAllBytes(path));
        SaveHeader.Read(reader);
        var raw = ChunkedZlib.Decompress(reader, out _);
        var outPath = args.Length > 2 ? args[2] : Path.ChangeExtension(path, ".body.bin");
        File.WriteAllBytes(outPath, raw);
        Console.WriteLine($"Wrote {raw.Length} bytes to {outPath}");
        return 0;
    }

    case "roundtrip-check":
    {
        var fileBytes = File.ReadAllBytes(path);
        var reader = new SaveReader(fileBytes);
        SaveHeader.Read(reader);
        var original = ChunkedZlib.Decompress(reader, out _);

        var save = SaveFile.Load(fileBytes);
        var rewritten = save.SerializeBody();
        int diff = FirstDifference(original, rewritten);
        if (diff < 0)
        {
            Console.WriteLine($"OK: body round-trips byte-identically ({original.Length} bytes).");
            return 0;
        }
        Console.WriteLine($"MISMATCH at body offset {diff} (original {original.Length} bytes, rewritten {rewritten.Length} bytes).");
        return 2;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 1;
}

static int FirstDifference(byte[] a, byte[] b)
{
    int n = Math.Min(a.Length, b.Length);
    int i = a.AsSpan(0, n).CommonPrefixLength(b.AsSpan(0, n));
    if (i < n) return i;
    return a.Length == b.Length ? -1 : n;
}
