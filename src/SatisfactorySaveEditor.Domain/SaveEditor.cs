using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Domain.Conveyors;
using SatisfactorySaveEditor.Domain.Deletion;
using SatisfactorySaveEditor.Domain.Inventory;
using SatisfactorySaveEditor.Domain.Map;
using SatisfactorySaveEditor.Domain.Players;
using SatisfactorySaveEditor.Domain.Power;
using SatisfactorySaveEditor.Domain.Progression;
using SatisfactorySaveEditor.Domain.Structures;

namespace SatisfactorySaveEditor.Domain;

/// <summary>
/// Game-level view over a loaded save: players, inventories and progression.
/// Game data is optional; features that need it (item names, stack sizes, schematic unlocks) require it.
/// </summary>
public sealed class SaveEditor
{
    private Dictionary<string, SaveObject>? _byInstanceName;

    public SaveEditor(SaveFile save, GameData.GameData? gameData = null)
    {
        Save = save;
        GameData = gameData;
        Players = new PlayerService(this);
        Progression = new ProgressionService(this);
        Containers = new ContainerService(this);
        Map = new MapService(this);
        Deletion = new DeletionService(this);
        Power = new PowerService(this);
        Structures = new StructureService(this);
        Conveyors = new ConveyorService(this);
        Sink = new SinkService(this);
        Depot = new DepotService(this);
    }

    public SaveFile Save { get; }
    public GameData.GameData? GameData { get; }
    public PlayerService Players { get; }
    public ProgressionService Progression { get; }
    public ContainerService Containers { get; }
    public MapService Map { get; }
    public DeletionService Deletion { get; }
    public PowerService Power { get; }
    public StructureService Structures { get; }
    public ConveyorService Conveyors { get; }
    public SinkService Sink { get; }
    public DepotService Depot { get; }

    /// <summary>Drops the instance-name index after objects were added or removed.</summary>
    internal void InvalidateIndex() => _byInstanceName = null;

    public GameData.GameData RequireGameData() =>
        GameData ?? throw new InvalidOperationException("Game data is not loaded. Locate the game's CommunityResources/Docs JSON first.");

    /// <summary>Finds an object by instance name, e.g. <c>Persistent_Level:PersistentLevel.Char_Player_C_0</c>.</summary>
    public SaveObject? Find(string? instanceName)
    {
        if (instanceName is null)
            return null;
        _byInstanceName ??= Save.Body.AllObjects
            .Where(o => o.InstanceName is not null)
            .GroupBy(o => o.InstanceName!)
            .ToDictionary(g => g.Key, g => g.First());
        return _byInstanceName.GetValueOrDefault(instanceName);
    }

    public SaveObject? Resolve(ObjectReference? reference) => reference is null || reference.IsEmpty ? null : Find(reference.PathName);

    /// <summary>Objects whose class name (the part after the last '.') equals <paramref name="className"/>.</summary>
    public IEnumerable<SaveObject> OfClass(string className) =>
        Save.Body.AllObjects.Where(o => o.ClassName == className);

    public SaveObject Single(string className) =>
        OfClass(className).FirstOrDefault() ?? throw new InvalidOperationException($"The save has no {className} object.");

    public Inventory.Inventory OpenInventory(SaveObject component) => new(this, component);

    /// <summary>Opens an inventory, or returns null when the component stores no slots (empty inventories may omit them).</summary>
    public Inventory.Inventory? TryOpenInventory(SaveObject component) =>
        component.IsParsed && component.Properties.Find("mInventoryStacks") is not null ? new(this, component) : null;

    /// <summary>Throws when an object's data could not be parsed and therefore cannot be edited safely.</summary>
    internal static SaveObject Editable(SaveObject obj) =>
        obj.IsParsed
            ? obj
            : throw new InvalidOperationException($"{obj.InstanceName} could not be parsed ({obj.ParseError ?? "no data"}) and cannot be edited.");
}
