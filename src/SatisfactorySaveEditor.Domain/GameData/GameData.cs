using System.Text.Json;
using System.Text.RegularExpressions;

namespace SatisfactorySaveEditor.Domain.GameData;

public sealed record ItemInfo(string ClassName, string? ClassPath, string DisplayName, int StackSize, string Form);

public sealed record RecipeInfo(string ClassName, string? ClassPath, string DisplayName);

/// <summary>One unlock granted by a schematic, e.g. recipes, inventory slots or emotes.</summary>
/// <param name="Kind">Unlock class name, e.g. <c>BP_UnlockRecipe_C</c>.</param>
/// <param name="Paths">Asset paths referenced by the unlock (recipes, items, emotes, schematics...).</param>
/// <param name="Amount">Numeric amount for slot unlocks, otherwise 0.</param>
public sealed record UnlockInfo(string Kind, IReadOnlyList<string> Paths, int Amount);

public sealed record SchematicInfo(
    string ClassName,
    string? ClassPath,
    string DisplayName,
    string Type,
    int TechTier,
    IReadOnlyList<UnlockInfo> Unlocks)
{
    public bool IsMilestone => Type == "EST_Milestone";
    public bool IsMam => Type == "EST_MAM";
    public bool IsAlternate => Type == "EST_Alternate";
    public bool IsShop => Type == "EST_ResourceSink";
}

/// <summary>
/// Game data read from the game's own <c>CommunityResources/Docs/*.json</c>, so it always matches the
/// installed game version. Entries are keyed by class name (e.g. <c>Desc_IronPlate_C</c>).
/// </summary>
public sealed partial class GameData
{
    private static readonly Dictionary<string, int> StackSizes = new()
    {
        ["SS_ONE"] = 1, ["SS_SMALL"] = 50, ["SS_MEDIUM"] = 100, ["SS_BIG"] = 200, ["SS_HUGE"] = 500, ["SS_FLUID"] = 50000,
    };

    private GameData() { }

    public IReadOnlyDictionary<string, ItemInfo> Items { get; private init; } = new Dictionary<string, ItemInfo>();
    public IReadOnlyDictionary<string, RecipeInfo> Recipes { get; private init; } = new Dictionary<string, RecipeInfo>();
    public IReadOnlyDictionary<string, SchematicInfo> Schematics { get; private init; } = new Dictionary<string, SchematicInfo>();

    /// <summary>Class name from a full path: <c>/Game/.../Desc_X.Desc_X_C</c> → <c>Desc_X_C</c>.</summary>
    public static string ClassNameOf(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot >= 0 ? path[(dot + 1)..] : path;
    }

    public ItemInfo? FindItem(string? classPathOrName) =>
        classPathOrName is not null && Items.TryGetValue(ClassNameOf(classPathOrName), out var item) ? item : null;

    public SchematicInfo? FindSchematic(string? classPathOrName) =>
        classPathOrName is not null && Schematics.TryGetValue(ClassNameOf(classPathOrName), out var s) ? s : null;

    public static GameData Load(string docsJsonPath) => Parse(File.ReadAllText(docsJsonPath));

    public static GameData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // Many entries lack FullName, so collect every asset path mentioned anywhere and index by class name.
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AssetPathRegex().Matches(json))
            paths.TryAdd(ClassNameOf(m.Value), m.Value);

        var items = new Dictionary<string, ItemInfo>();
        var recipes = new Dictionary<string, RecipeInfo>();
        var schematics = new Dictionary<string, SchematicInfo>();

        foreach (var group in doc.RootElement.EnumerateArray())
        {
            string nativeClass = group.GetProperty("NativeClass").GetString() ?? "";
            foreach (var entry in group.GetProperty("Classes").EnumerateArray())
            {
                string className = Str(entry, "ClassName");
                if (className.Length == 0)
                    continue;
                string? path = PathFromFullName(Str(entry, "FullName")) ?? paths.GetValueOrDefault(className);
                string name = Str(entry, "mDisplayName");

                if (nativeClass.EndsWith("FGSchematic'"))
                {
                    schematics[className] = new SchematicInfo(className, path, name, Str(entry, "mType"),
                        int.TryParse(Str(entry, "mTechTier"), out int tier) ? tier : 0, ParseUnlocks(entry));
                }
                else if (nativeClass.EndsWith("FGRecipe'"))
                {
                    recipes[className] = new RecipeInfo(className, path, name);
                }
                else if (entry.TryGetProperty("mStackSize", out _) && !nativeClass.Contains("BuildingDescriptor")
                         && !nativeClass.Contains("VehicleDescriptor"))
                {
                    int stack = int.TryParse(Str(entry, "mCachedStackSize"), out int cached) && cached > 0
                        ? cached
                        : StackSizes.GetValueOrDefault(Str(entry, "mStackSize"), 1);
                    items[className] = new ItemInfo(className, path, name, stack, Str(entry, "mForm"));
                }
            }
        }

        return new GameData { Items = items, Recipes = recipes, Schematics = schematics };
    }

    private static List<UnlockInfo> ParseUnlocks(JsonElement schematic)
    {
        var unlocks = new List<UnlockInfo>();
        if (!schematic.TryGetProperty("mUnlocks", out var list) || list.ValueKind != JsonValueKind.Array)
            return unlocks;
        foreach (var unlock in list.EnumerateArray())
        {
            string kind = Str(unlock, "Class");
            var unlockPaths = new List<string>();
            int amount = 0;
            foreach (var field in unlock.EnumerateObject())
            {
                if (field.Name == "Class" || field.Value.ValueKind != JsonValueKind.String)
                    continue;
                string value = field.Value.GetString() ?? "";
                if (field.Name.StartsWith("mNum") && int.TryParse(value, out int n))
                    amount = n;
                else
                    unlockPaths.AddRange(AssetPathRegex().Matches(value).Select(m => m.Value));
            }
            unlocks.Add(new UnlockInfo(kind, unlockPaths, amount));
        }
        return unlocks;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>"BlueprintGeneratedClass /Game/A/B.B_C" → "/Game/A/B.B_C".</summary>
    private static string? PathFromFullName(string fullName)
    {
        int space = fullName.IndexOf(' ');
        var path = space >= 0 ? fullName[(space + 1)..] : fullName;
        return path.StartsWith('/') ? path : null;
    }

    /// <summary>Matches blueprint class paths such as /Game/FactoryGame/Recipes/X/Recipe_A.Recipe_A_C.</summary>
    [GeneratedRegex(@"/Game/[A-Za-z0-9_\-/]+\.[A-Za-z0-9_\-]+_C")]
    private static partial Regex AssetPathRegex();
}
