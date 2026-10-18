using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.Domain.Power;

/// <summary>A power line and the buildings at each end (null when an end cannot be resolved).</summary>
public sealed record PowerLine(ActorObject Line, SaveObject? SourceConnection, SaveObject? TargetConnection,
    ActorObject? SourceBuilding, ActorObject? TargetBuilding)
{
    public bool IsComplete => SourceBuilding is not null && TargetBuilding is not null;

    /// <summary>e.g. "Power Pole Mk1 ↔ Constructor Mk1".</summary>
    public string Description => $"{Name(SourceBuilding)} ↔ {Name(TargetBuilding)}";

    private static string Name(ActorObject? actor) =>
        actor is null ? "(missing)" : ContainerService.FriendlyClassName(actor.ClassName);
}

/// <summary>Power lines decoded from their trailing data, resolved to the connected buildings.</summary>
public sealed class PowerService(SaveEditor editor)
{
    public IReadOnlyList<PowerLine> Lines =>
        editor.Save.Body.AllObjects.OfType<ActorObject>()
            .Where(a => a.Trailing is PowerLineTrailing)
            .Select(Resolve)
            .ToList();

    /// <summary>The power lines connected to any power connection of <paramref name="building"/>.</summary>
    public IReadOnlyList<PowerLine> LinesOf(ActorObject building) =>
        Lines.Where(l => l.SourceBuilding == building || l.TargetBuilding == building).ToList();

    public PowerLine Resolve(ActorObject line)
    {
        var ends = line.Trailing as PowerLineTrailing
                   ?? throw new ArgumentException($"{line.InstanceName} is not a decoded power line.", nameof(line));
        var source = editor.Find(ends.Source.PathName);
        var target = editor.Find(ends.Target.PathName);
        return new PowerLine(line, source, target, OwnerOf(source), OwnerOf(target));
    }

    private ActorObject? OwnerOf(SaveObject? connection) => connection switch
    {
        ComponentObject c => editor.Find(c.ParentActorName) as ActorObject,
        ActorObject a => a,
        _ => null,
    };
}
