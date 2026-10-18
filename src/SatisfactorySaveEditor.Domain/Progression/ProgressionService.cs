using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Domain.GameData;

namespace SatisfactorySaveEditor.Domain.Progression;

/// <summary>Outcome of unlocking one schematic.</summary>
/// <param name="Applied">Unlock effects written to the save.</param>
/// <param name="Skipped">Unlock effects the editor cannot apply (e.g. items given on purchase).</param>
public sealed record UnlockResult(SchematicInfo Schematic, bool AlreadyPurchased, IReadOnlyList<string> Applied, IReadOnlyList<string> Skipped);

/// <summary>
/// Schematics (milestones, MAM research, alternate recipes, AWESOME Shop items) and the unlocks they grant.
/// Unlocking adds the schematic to the purchased list and applies the unlock effects that the save stores
/// directly: recipes, item descriptors, inventory/arm slots, emotes and the map/efficiency/overclock flags.
/// </summary>
public sealed class ProgressionService(SaveEditor editor)
{
    private const string MainInventoryBaseSlots = "mNumTotalInventorySlots";
    private const string ArmSlotsProperty = "mNumTotalArmEquipmentSlots";

    private static readonly Dictionary<string, string> FlagUnlocks = new()
    {
        ["BP_UnlockMap_C"] = "mIsMapUnlocked",
        ["BP_UnlockBuildEfficiency_C"] = "mIsBuildingEfficiencyUnlocked",
        ["BP_UnlockBuildOverclock_C"] = "mIsBuildingOverclockUnlocked",
    };

    private static readonly HashSet<string> NoEffectUnlocks = ["BP_UnlockInfoOnly_C", "BP_UnlockCheckmark_C"];

    private SaveObject SchematicManager => SaveEditor.Editable(editor.Single("BP_SchematicManager_C"));
    private SaveObject RecipeManager => SaveEditor.Editable(editor.Single("FGRecipeManager"));
    private SaveObject UnlockSubsystem => SaveEditor.Editable(editor.Single("BP_UnlockSubsystem_C"));

    public IReadOnlyList<string> PurchasedSchematics => PathsIn(SchematicManager, "mPurchasedSchematics");
    public IReadOnlyList<string> AvailableRecipes => PathsIn(RecipeManager, "mAvailableRecipes");

    public bool IsPurchased(string schematicClassOrPath)
    {
        string className = GameData.GameData.ClassNameOf(schematicClassOrPath);
        return PurchasedSchematics.Any(p => GameData.GameData.ClassNameOf(p) == className);
    }

    public int InventorySlots =>
        UnlockSubsystem.Properties.Get<IntValue>(MainInventoryBaseSlots)?.Value ?? (editor.Players.All is [var first, ..] ? first.Inventory.Size : 0);

    public int ArmEquipmentSlots =>
        UnlockSubsystem.Properties.Get<IntValue>(ArmSlotsProperty)?.Value
        ?? (editor.Players.All is [var first, ..] ? first.EquipmentSlots.GetValueOrDefault("Hands")?.Size : null) ?? 1;

    /// <summary>Unlocks a schematic by class name (e.g. <c>Schematic_3-4_C</c>) or path. Requires game data.</summary>
    public UnlockResult Unlock(string schematicClassOrPath)
    {
        var schematic = editor.RequireGameData().FindSchematic(schematicClassOrPath)
                        ?? throw new ArgumentException($"Unknown schematic '{schematicClassOrPath}'.", nameof(schematicClassOrPath));
        if (schematic.ClassPath is null)
            throw new InvalidOperationException($"No asset path is known for {schematic.ClassName}.");
        if (IsPurchased(schematic.ClassName))
            return new UnlockResult(schematic, true, [], []);

        var applied = new List<string>();
        var skipped = new List<string>();
        AddPaths(SchematicManager, "mPurchasedSchematics", [schematic.ClassPath]);

        foreach (var unlock in schematic.Unlocks)
        {
            switch (unlock.Kind)
            {
                case "BP_UnlockRecipe_C":
                case "BP_UnlockBlueprints_C":
                    applied.Add($"{AddPaths(RecipeManager, "mAvailableRecipes", unlock.Paths)} recipes");
                    break;
                case "BP_UnlockItemDescriptor_C":
                    applied.Add($"{AddPaths(RecipeManager, "mAvailableItemDescriptors", unlock.Paths)} item descriptors");
                    break;
                case "BP_UnlockEmote_C":
                    applied.Add($"{AddPaths(UnlockSubsystem, "mUnlockedEmotes", unlock.Paths)} emotes");
                    break;
                case "BP_UnlockInventorySlot_C":
                    SetInventorySlots(InventorySlots + unlock.Amount);
                    applied.Add($"{unlock.Amount} inventory slots");
                    break;
                case "BP_UnlockArmEquipmentSlot_C":
                    SetArmEquipmentSlots(ArmEquipmentSlots + unlock.Amount);
                    applied.Add($"{unlock.Amount} hand slots");
                    break;
                case var kind when FlagUnlocks.TryGetValue(kind, out var flag):
                    UnlockSubsystem.Properties.GetOrAdd(flag, PropertyTypes.Bool, () => new BoolValue(false)).Value = true;
                    applied.Add(flag);
                    break;
                case var kind when NoEffectUnlocks.Contains(kind):
                    break;
                default:
                    skipped.Add(unlock.Kind);
                    break;
            }
        }
        return new UnlockResult(schematic, false, applied, skipped);
    }

    /// <summary>Unlocks every not-yet-purchased schematic matching <paramref name="filter"/>, lowest tier first.</summary>
    public IReadOnlyList<UnlockResult> UnlockWhere(Func<SchematicInfo, bool> filter) =>
        editor.RequireGameData().Schematics.Values
            .Where(s => filter(s) && s.ClassPath is not null && !IsPurchased(s.ClassName))
            .OrderBy(s => s.TechTier).ThenBy(s => s.ClassName)
            .Select(s => Unlock(s.ClassName))
            .ToList();

    /// <summary>Sets the main inventory size and resizes every player's inventory to match.</summary>
    public void SetInventorySlots(int total)
    {
        foreach (var player in editor.Players.All.Where(p => p.Pawn is not null))
            player.Inventory.Resize(total);
        UnlockSubsystem.Properties.GetOrAdd(MainInventoryBaseSlots, PropertyTypes.Int, () => new IntValue(0)).Value = total;
    }

    /// <summary>Sets the number of hand equipment slots and resizes every player's hand slot inventory.</summary>
    public void SetArmEquipmentSlots(int total)
    {
        foreach (var player in editor.Players.All.Where(p => p.Pawn is not null))
            player.EquipmentSlots.GetValueOrDefault("Hands")?.Resize(total);
        UnlockSubsystem.Properties.GetOrAdd(ArmSlotsProperty, PropertyTypes.Int, () => new IntValue(0)).Value = total;
    }

    private static List<string> PathsIn(SaveObject obj, string property) =>
        obj.Properties.Get<ArrayValue>(property)?.Elements.OfType<ObjectValue>()
            .Select(v => v.Value.PathName).OfType<string>().ToList() ?? [];

    /// <summary>Adds class references to an object array, skipping ones already present. Returns how many were added.</summary>
    private static int AddPaths(SaveObject obj, string property, IEnumerable<string> paths)
    {
        var array = obj.Properties.GetOrAdd(property, PropertyTypes.ArrayOf(PropertyTypes.Object),
            () => new ArrayValue(PropertyTypes.Object));
        var existing = array.Elements.OfType<ObjectValue>().Select(v => v.Value.PathName).ToHashSet();
        int added = 0;
        foreach (var path in paths)
        {
            if (existing.Add(path))
            {
                array.Elements.Add(new ObjectValue(new ObjectReference(null, path)));
                added++;
            }
        }
        return added;
    }
}
