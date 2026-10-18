using System.Text;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.Domain.Deletion;

/// <summary>A reference that will be cleared (or removed from its array/set/map) when the plan runs.</summary>
public sealed record ClearedReference(SaveObject Owner, string Description, Action Apply);

/// <summary>
/// What deleting an actor involves: the objects removed (the actor, its components and dependent sidecars
/// such as attached power lines) and the references elsewhere that get cleared. When
/// <see cref="BlockedReason"/> is set the deletion is not possible.
/// </summary>
public sealed class DeletionPlan
{
    internal DeletionPlan(IReadOnlyList<ActorObject> targets) => Targets = targets;

    /// <summary>The actors the user asked to delete.</summary>
    public IReadOnlyList<ActorObject> Targets { get; }
    public ActorObject Target => Targets[0];
    public string? BlockedReason { get; internal set; }
    public bool CanDelete => BlockedReason is null;
    public List<SaveObject> ObjectsToRemove { get; } = [];
    public List<ClearedReference> ReferencesToClear { get; } = [];
    /// <summary>Selected actors left out because they can't be deleted (players, map-placed objects).</summary>
    public List<(ActorObject Actor, string Reason)> Skipped { get; } = [];

    /// <summary>Removed actors other than the targets (e.g. attached power lines, conveyor chains).</summary>
    public IEnumerable<ActorObject> AlsoDeleted =>
        ObjectsToRemove.OfType<ActorObject>().Where(a => !Targets.Contains(a) && !Skipped.Any(s => s.Actor == a));

    public int ComponentCount => ObjectsToRemove.Count(o => o is ComponentObject);
}

/// <summary>
/// Deletes built actors (storage, machines, vehicles, crates...) together with their components, cleaning up
/// references to them. Deletion is refused when it cannot be done cleanly: players, actors placed in the map
/// itself (the game would recreate them), and actors referenced from data the editor cannot edit yet
/// (e.g. conveyor belts referenced by conveyor chain actors).
/// </summary>
public sealed class DeletionService(SaveEditor editor)
{
    /// <summary>Actors that only exist to connect or describe another actor and are deleted along with it.</summary>
    private static readonly HashSet<string> CascadeClasses = ["Build_PowerLine_C", "FGWheeledVehicleIdentifier"];

    /// <summary>
    /// Sidecars are deleted with what they point at. Conveyor chains are included: removing a belt removes
    /// its whole chain (the items on it are lost) and the game rebuilds chains for the remaining belts.
    /// </summary>
    private static bool IsCascade(ActorObject actor) =>
        CascadeClasses.Contains(actor.ClassName) || actor.Trailing is ConveyorChainTrailing;

    public DeletionPlan Plan(ActorObject target) => Plan([target]);

    /// <summary>
    /// Plans deleting several actors at once. Actors that can't be deleted are listed in
    /// <see cref="DeletionPlan.Skipped"/>; with a single target the plan is blocked instead.
    /// </summary>
    public DeletionPlan Plan(IReadOnlyList<ActorObject> targets)
    {
        var plan = new DeletionPlan(targets);
        var deletable = new List<ActorObject>();
        foreach (var target in targets.Distinct())
        {
            if (RefusalReason(target) is { } reason)
                plan.Skipped.Add((target, reason));
            else
                deletable.Add(target);
        }
        if (deletable.Count == 0)
            return Block(plan, targets.Count == 1 ? plan.Skipped[0].Reason : "None of the selected objects can be deleted.");

        var componentsByOwner = editor.Save.Body.AllObjects.OfType<ComponentObject>()
            .Where(c => c.ParentActorName is not null)
            .GroupBy(c => c.ParentActorName!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Remove the targets and their components, then cascade into sidecars (power lines, conveyor chains,
        // vehicle identifiers) that point at anything removed, round by round until nothing new is added.
        var actors = new HashSet<ActorObject>();
        var removed = new HashSet<SaveObject>();
        var frontier = deletable;
        while (frontier.Count > 0)
        {
            foreach (var actor in frontier)
            {
                actors.Add(actor);
                removed.Add(actor);
                foreach (var component in componentsByOwner.GetValueOrDefault(actor.InstanceName ?? "", []))
                    removed.Add(component);
                foreach (var reference in actor.Components)
                    if (editor.Find(reference.PathName) is { } component)
                        removed.Add(component);
            }
            var names = NamesOf(removed);
            frontier = editor.Save.Body.AllObjects.OfType<ActorObject>()
                .Where(other => !actors.Contains(other) && IsCascade(other) && Mentions(other, names))
                .ToList();
        }

        var removedNames = NamesOf(removed);
        foreach (var obj in editor.Save.Body.AllObjects)
        {
            if (removed.Contains(obj))
                continue;
            if (!obj.IsParsed)
            {
                if (obj.RawData.Length > 0 && ContainsAny(obj.RawData, removedNames))
                    return Block(plan, $"{Friendly(obj)} refers to it in data the editor can't read.");
                continue;
            }
            if (TrailingMentions(obj, removedNames))
                return Block(plan, $"{Friendly(obj)} refers to it in data the editor can't edit yet.");
            CollectReferences(obj, obj.Properties, removedNames, plan.ReferencesToClear);
        }

        plan.ObjectsToRemove.AddRange(editor.Save.Body.AllObjects.Where(removed.Contains));
        return plan;
    }

    /// <summary>Applies a plan: clears references, then removes the objects from their levels.</summary>
    public void Execute(DeletionPlan plan)
    {
        if (!plan.CanDelete)
            throw new InvalidOperationException(plan.BlockedReason);
        foreach (var reference in plan.ReferencesToClear)
            reference.Apply();
        var toRemove = plan.ObjectsToRemove.ToHashSet();
        foreach (var level in editor.Save.Body.Levels)
            level.Objects.RemoveAll(toRemove.Contains);
        editor.InvalidateIndex();
    }

    private static DeletionPlan Block(DeletionPlan plan, string reason)
    {
        plan.BlockedReason = reason;
        return plan;
    }

    private static string? RefusalReason(ActorObject actor) => actor switch
    {
        { ClassName: "Char_Player_C" } => "Players can't be deleted.",
        { WasPlacedInLevel: not 0 } => "This object is part of the map itself. Removing it from the save would make the game recreate it.",
        _ => null,
    };

    private static HashSet<string> NamesOf(IEnumerable<SaveObject> objects) =>
        objects.Select(o => o.InstanceName).OfType<string>().ToHashSet();

    private static bool Mentions(SaveObject obj, HashSet<string> names) =>
        TrailingMentions(obj, names)
        || (obj.IsParsed && PropertyWalker.Descendants(obj.Properties).OfType<ObjectValue>().Any(v => v.Value.PathName is { } p && names.Contains(p)));

    /// <summary>
    /// Whether an object's trailing data refers to any of the names. Decoded trailing data is checked through
    /// its references; raw bytes are searched.
    /// </summary>
    private static bool TrailingMentions(SaveObject obj, HashSet<string> names)
    {
        bool Has(ObjectReference r) => r.PathName is { } p && names.Contains(p);
        return obj.Trailing switch
        {
            PowerLineTrailing wire => Has(wire.Source) || Has(wire.Target),
            ConveyorChainTrailing chain => Has(chain.FirstBelt) || Has(chain.LastBelt) || chain.Segments.Any(s => Has(s.Belt)),
            ConveyorItemsTrailing => false, // item classes only, no object references
            LightweightBuildablesTrailing lightweight => lightweight.Classes.Any(c => c.Instances.Any(i => Has(i.BlueprintProxy))),
            _ => ContainsAny(obj.TrailingData, names),
        };
    }

    /// <summary>
    /// Whether serialized bytes contain any of the names as an FString. Matching the null terminator avoids
    /// false hits on longer names ("…_C_182" inside "…_C_1820").
    /// </summary>
    private static bool ContainsAny(byte[] data, HashSet<string> names)
    {
        if (data.Length == 0)
            return false;
        var text = Encoding.Latin1.GetString(data);
        return names.Any(n => text.Contains(n + '\0', StringComparison.Ordinal));
    }

    /// <summary>Finds object references to removed objects and records how to clear each one.</summary>
    private static void CollectReferences(SaveObject owner, PropertyList properties, HashSet<string> names, List<ClearedReference> into)
    {
        foreach (var property in properties)
        {
            if (property.Value is ObjectValue ov && IsRemoved(ov, names))
            {
                var p = property;
                into.Add(new(owner, $"{Friendly(owner)}: {property.Name}", () => ((ObjectValue)p.Value).Value = ObjectReference.Empty));
                continue;
            }
            CollectInValue(owner, property.Name ?? "?", property.Value, names, into);
        }
    }

    private static void CollectInValue(SaveObject owner, string path, PropertyValue value, HashSet<string> names, List<ClearedReference> into)
    {
        switch (value)
        {
            case PropertyListStruct s:
                CollectReferences(owner, s.Properties, names, into);
                break;
            case ArrayValue a:
                foreach (var element in a.Elements.ToList())
                {
                    if (element is ObjectValue ov && IsRemoved(ov, names))
                        into.Add(new(owner, $"{Friendly(owner)}: {path} entry", () => a.Elements.Remove(element)));
                    else
                        CollectInValue(owner, path, element, names, into);
                }
                break;
            case SetValue s:
                foreach (var element in s.Elements.ToList())
                    if (element is ObjectValue ov && IsRemoved(ov, names))
                        into.Add(new(owner, $"{Friendly(owner)}: {path} entry", () => s.Elements.Remove(element)));
                break;
            case MapValue m:
                foreach (var entry in m.Entries.ToList())
                {
                    if (entry.Key is ObjectValue key && IsRemoved(key, names))
                        into.Add(new(owner, $"{Friendly(owner)}: {path} entry", () => m.Entries.Remove(entry)));
                    else if (entry.Value is ObjectValue val && IsRemoved(val, names))
                        into.Add(new(owner, $"{Friendly(owner)}: {path} entry", () => val.Value = ObjectReference.Empty));
                    else
                        CollectInValue(owner, path, entry.Value, names, into);
                }
                break;
        }
    }

    private static bool IsRemoved(ObjectValue value, HashSet<string> names) =>
        value.Value.PathName is { } path && names.Contains(path);

    private static string Friendly(SaveObject obj)
    {
        var name = obj.InstanceName ?? "";
        return $"{ContainerService.FriendlyClassName(obj.ClassName)} ({name[(name.LastIndexOf('.') + 1)..]})";
    }
}
