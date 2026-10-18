using System.Numerics;
using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Domain.Inventory;

public enum ContainerCategory { Storage, Vehicles, Machines, Logistics, World }

/// <summary>A named inventory belonging to a container, e.g. "Storage" or "Fuel".</summary>
public sealed record ContainerInventory(string Name, Inventory Inventory);

/// <summary>An actor that owns one or more inventories: storage, vehicles, machines, crates...</summary>
public sealed class Container
{
    internal Container(ActorObject owner, ContainerCategory category, IReadOnlyList<ContainerInventory> inventories)
    {
        Owner = owner;
        Category = category;
        Inventories = inventories;
    }

    public ActorObject Owner { get; }
    public ContainerCategory Category { get; }
    public IReadOnlyList<ContainerInventory> Inventories { get; }

    /// <summary>Readable type, e.g. "Storage Container Mk2" from Build_StorageContainerMk2_C.</summary>
    public string TypeName => ContainerService.FriendlyClassName(Owner.ClassName);
    /// <summary>Instance name without the level prefix, e.g. "Build_StorageContainerMk2_C_2147471234".</summary>
    public string InstanceName => Owner.InstanceName?[(Owner.InstanceName.LastIndexOf('.') + 1)..] ?? "";
    public Vector3 Position => Owner.Position;
    public int UsedSlots => Inventories.Sum(i => i.Inventory.Slots.Count(s => !s.IsEmpty));
    public int TotalSlots => Inventories.Sum(i => i.Inventory.Size);

    public override string ToString() => $"{TypeName} ({InstanceName})";
}

/// <summary>
/// Finds every inventory outside the players' own (those are in <see cref="Players.Player"/>) and groups
/// them by owning actor. The Dimensional Depot is not included: its contents live in the central storage
/// subsystem, whose format has not been observed yet.
/// </summary>
public sealed class ContainerService(SaveEditor editor)
{
    private static readonly string[] VehicleMarkers =
        ["BP_Tractor", "BP_Truck", "BP_Explorer", "BP_Golfcart", "BP_DroneTransport", "FreightWagon", "Locomotive",
         "TruckStation", "DroneStation", "TrainDockingStation"];

    public IReadOnlyList<Container> All
    {
        get
        {
            var containers = new List<Container>();
            var components = editor.Save.Body.AllObjects.OfType<ComponentObject>()
                .Where(c => c.IsParsed && c.Properties.Find("mInventoryStacks") is not null);
            foreach (var group in components.GroupBy(c => c.ParentActorName))
            {
                if (editor.Find(group.Key) is not ActorObject owner || owner.ClassName == "Char_Player_C")
                    continue;
                var inventories = group
                    .Select(c => new ContainerInventory(InventoryName(c), editor.OpenInventory(c)))
                    .OrderBy(i => i.Name == "Storage" || i.Name == "Inventory" ? 0 : 1).ThenBy(i => i.Name)
                    .ToList();
                containers.Add(new Container(owner, CategoryOf(owner.ClassName), inventories));
            }
            return containers.OrderBy(c => c.Category).ThenBy(c => c.TypeName).ThenBy(c => c.InstanceName).ToList();
        }
    }

    public static ContainerCategory CategoryOf(string className)
    {
        if (VehicleMarkers.Any(className.Contains))
            return ContainerCategory.Vehicles;
        if (className.Contains("ConveyorAttachment") || className.Contains("Pipeline"))
            return ContainerCategory.Logistics;
        if (className.Contains("Storage") || className.Contains("Potty"))
            return ContainerCategory.Storage;
        if (className.StartsWith("Build_", StringComparison.OrdinalIgnoreCase))
            return ContainerCategory.Machines;
        return ContainerCategory.World;
    }

    /// <summary>"Build_StorageContainerMk2_C" → "Storage Container Mk2".</summary>
    public static string FriendlyClassName(string className)
    {
        var name = className;
        foreach (var prefix in new[] { "Build_", "BUILD_", "BP_", "Char_" })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) { name = name[prefix.Length..]; break; }
        if (name.EndsWith("_C", StringComparison.Ordinal))
            name = name[..^2];
        var words = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_') { words.Append(' '); continue; }
            bool boundary = i > 0 && char.IsUpper(c) && name[i - 1] != '_'
                            && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (boundary)
                words.Append(' ');
            words.Append(c);
        }
        return words.ToString().Replace("  ", " ").Trim();
    }

    private static string InventoryName(ComponentObject component)
    {
        var name = component.InstanceName?[(component.InstanceName.LastIndexOf('.') + 1)..] ?? "";
        return name switch
        {
            "StorageInventory" => "Storage",
            "FuelInventory" => "Fuel",
            "InputInventory" => "Input",
            "OutputInventory" => "Output",
            "InventoryPotential" => "Shards & Somersloops",
            "inventory" => "Inventory",
            _ => name,
        };
    }
}
