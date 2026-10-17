using System.Diagnostics;
using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Core.Compression;
using SatisfactorySaveEditor.Core.IO;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Domain;
using SatisfactorySaveEditor.Domain.GameData;

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

    case "stats":
    {
        var save = SaveFile.Load(path);
        var objects = save.Body.AllObjects.ToList();
        var withData = objects.Where(o => o.IsParsed || o.RawData.Length > 0).ToList();
        Console.WriteLine($"Objects with data : {withData.Count}");
        Console.WriteLine($"Parsed            : {withData.Count(o => o.IsParsed)}");
        foreach (var g in withData.Where(o => !o.IsParsed).GroupBy(o => (o.ClassName, o.ParseError)).OrderByDescending(g => g.Count()).Take(15))
            Console.WriteLine($"  UNPARSED {g.Count(),6}  {g.Key.ClassName}: {g.Key.ParseError}");

        var values = objects.Where(o => o.IsParsed).SelectMany(o => PropertyWalker.Descendants(o.Properties)).ToList();
        Console.WriteLine($"Values            : {values.Count}");
        var raws = values.OfType<RawValue>().ToList();
        Console.WriteLine($"Raw values        : {raws.Count}");
        foreach (var g in raws.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()).Take(20))
            Console.WriteLine($"  {g.Count(),6}  {g.Key}");
        var rawStructs = values.OfType<RawStruct>().ToList();
        Console.WriteLine($"Raw structs       : {rawStructs.Count}");
        foreach (var g in rawStructs.GroupBy(r => r.StructName).OrderByDescending(g => g.Count()).Take(20))
            Console.WriteLine($"  {g.Count(),6}  {g.Key}");
        var stateful = values.OfType<InventoryItemStruct>().Where(i => i.HasState != 0).ToList();
        Console.WriteLine($"Items with state  : {stateful.Count}");
        foreach (var i in stateful.Take(3))
            Console.WriteLine($"  {i.ItemClass} {i.StateType}: {(i.State is null ? $"RAW [{i.StateData.Length}]" : string.Join(", ", i.State))}");
        var trailing = objects.Where(o => o.IsParsed && o.TrailingData.Length > 0).ToList();
        Console.WriteLine($"Objects with trailing data: {trailing.Count}");
        foreach (var g in trailing.GroupBy(o => o.ClassName).OrderByDescending(g => g.Count()).Take(15))
            Console.WriteLine($"  {g.Count(),6}  {g.Key} (avg {g.Average(o => o.TrailingData.Length):F0} bytes)");
        return 0;
    }

    case "dump":
    {
        // dump <save> <class-or-instance-substring> [max objects]
        var save = SaveFile.Load(path);
        var filter = args.Length > 2 ? args[2] : "";
        int max = args.Length > 3 ? int.Parse(args[3]) : 3;
        foreach (var o in save.Body.AllObjects.Where(o => (o.TypePath ?? "").Contains(filter) || (o.InstanceName ?? "").Contains(filter)).Take(max))
        {
            Console.WriteLine($"{o.TypePath}  {o.InstanceName}  (v{o.ObjectSaveVersion}{(o.UsesLegacyTags ? ", legacy tags" : "")})");
            if (o is ActorObject a)
                Console.WriteLine($"  transform: pos {a.Position} rot {a.Rotation} scale {a.Scale}; parent {a.Parent}; {a.Components.Count} components");
            foreach (var p in o.Properties)
                DumpProperty($"{p.Name}{(p.Tag.ArrayIndex != 0 ? $"[{p.Tag.ArrayIndex}]" : "")} ({p.Type})", p.Value, "  ");
            if (o.TrailingData.Length > 0)
                Console.WriteLine($"  trailing [{o.TrailingData.Length}]: {Convert.ToHexString(o.TrailingData.AsSpan(0, Math.Min(64, o.TrailingData.Length)))}");
        }
        return 0;
    }

    case "classes":
    {
        var save = SaveFile.Load(path);
        var filter = args.Length > 2 ? args[2] : "";
        foreach (var g in save.Body.AllObjects.Where(o => (o.TypePath ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(o => o.TypePath).OrderBy(g => g.Key))
            Console.WriteLine($"{g.Count(),7}  {g.Key}");
        return 0;
    }

    case "players":
    {
        var editor = new SaveEditor(SaveFile.Load(path), LoadGameData());
        foreach (var player in editor.Players.All)
        {
            Console.WriteLine($"{player.Name}  (state {player.State.InstanceName})");
            if (player.Pawn is null)
            {
                Console.WriteLine("  no character");
                continue;
            }
            Console.WriteLine($"  position {player.Position}, health {player.Health}");
            foreach (var slot in player.Inventory.Slots.Where(s => !s.IsEmpty))
                Console.WriteLine($"  [{slot.Index,2}] {slot.Count,5} x {slot.DisplayName}{(slot.HasState ? " (state)" : "")}");
            foreach (var (name, inventory) in player.EquipmentSlots)
                Console.WriteLine($"  {name}: {string.Join(", ", inventory.Slots.Where(s => !s.IsEmpty).Select(s => s.DisplayName).DefaultIfEmpty("(empty)"))}");
        }
        return 0;
    }

    case "progression":
    {
        var gameData = LoadGameData() ?? throw new InvalidOperationException("Game data not found.");
        Console.WriteLine($"Game data: {gameData.Items.Count} items ({gameData.Items.Values.Count(i => i.ClassPath is null)} without path), " +
                          $"{gameData.Recipes.Count} recipes, {gameData.Schematics.Count} schematics");
        var editor = new SaveEditor(SaveFile.Load(path), gameData);
        var purchased = editor.Progression.PurchasedSchematics;
        Console.WriteLine($"Purchased schematics: {purchased.Count}, recipes: {editor.Progression.AvailableRecipes.Count}, " +
                          $"inventory slots: {editor.Progression.InventorySlots}, hand slots: {editor.Progression.ArmEquipmentSlots}");
        foreach (var g in purchased.Select(p => gameData.FindSchematic(p)).GroupBy(s => s?.Type ?? "(unknown)"))
            Console.WriteLine($"  {g.Key,-18} {g.Count(),4}  e.g. {string.Join(", ", g.Take(3).Select(s => s?.DisplayName))}");
        return 0;
    }

    case "unlock":
    {
        // unlock <save> <milestones|mam|alternates|shop|SchematicClassName> <output.sav>
        if (args.Length < 4)
            throw new ArgumentException("Usage: unlock <save> <milestones|mam|alternates|shop|SchematicClassName> <output.sav>");
        var gameData = LoadGameData() ?? throw new InvalidOperationException("Game data not found.");
        var editor = new SaveEditor(SaveFile.Load(path), gameData);
        var results = args[2] switch
        {
            "milestones" => editor.Progression.UnlockWhere(s => s.IsMilestone),
            "mam" => editor.Progression.UnlockWhere(s => s.IsMam),
            "alternates" => editor.Progression.UnlockWhere(s => s.IsAlternate),
            "shop" => editor.Progression.UnlockWhere(s => s.IsShop),
            var name => [editor.Progression.Unlock(name)],
        };
        foreach (var r in results)
            Console.WriteLine($"{r.Schematic.DisplayName}: {(r.AlreadyPurchased ? "already purchased" : string.Join(", ", r.Applied.DefaultIfEmpty("-")))}");
        foreach (var g in results.SelectMany(r => r.Skipped).GroupBy(k => k))
            Console.WriteLine($"  skipped {g.Count(),3} x {g.Key}");
        editor.Save.Save(args[3]);
        Console.WriteLine($"Unlocked {results.Count(r => !r.AlreadyPurchased)} schematics; wrote {args[3]}");
        return 0;
    }

    case "raw-structs":
    {
        var save = SaveFile.Load(path);
        foreach (var o in save.Body.AllObjects.Where(o => o.IsParsed))
            foreach (var s in PropertyWalker.Descendants(o.Properties).OfType<RawStruct>())
                Console.WriteLine($"{o.ClassName} {s.StructName} [{s.Bytes.Length}]: {Convert.ToHexString(s.Bytes)}");
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

static GameData? LoadGameData()
{
    var docs = Environment.GetEnvironmentVariable("SSE_DOCS") ?? GameDataLocator.FindDocsJson();
    return docs is null ? null : GameData.Load(docs);
}

static void DumpProperty(string? name, PropertyValue value, string indent)
{
    const int maxElements = 4;
    switch (value)
    {
        case PropertyListStruct s:
            Console.WriteLine($"{indent}{name}: {s.StructName}");
            foreach (var p in s.Properties)
                DumpProperty(p.Name, p.Value, indent + "  ");
            break;
        case ArrayValue a:
            Console.WriteLine($"{indent}{name}: {a}");
            for (int i = 0; i < Math.Min(maxElements, a.Elements.Count); i++)
                DumpProperty($"[{i}]", a.Elements[i], indent + "  ");
            if (a.Elements.Count > maxElements)
                Console.WriteLine($"{indent}  ... {a.Elements.Count - maxElements} more");
            break;
        case MapValue m:
            Console.WriteLine($"{indent}{name}: {m}");
            foreach (var (k, v) in m.Entries.Take(maxElements))
                DumpProperty($"[{k}]", v, indent + "  ");
            break;
        default:
            Console.WriteLine($"{indent}{name} = {value}");
            break;
    }
}

static int FirstDifference(byte[] a, byte[] b)
{
    int n = Math.Min(a.Length, b.Length);
    int i = a.AsSpan(0, n).CommonPrefixLength(b.AsSpan(0, n));
    if (i < n) return i;
    return a.Length == b.Length ? -1 : n;
}
