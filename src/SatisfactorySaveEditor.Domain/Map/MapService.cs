using System.Numerics;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Domain.Inventory;
using SatisfactorySaveEditor.Domain.Players;

namespace SatisfactorySaveEditor.Domain.Map;

/// <summary>Marker layers, drawn in this order (later kinds on top).</summary>
public enum MapMarkerKind { Building, Conveyor, Logistics, Machine, World, Storage, Vehicle, Player }

/// <summary>
/// Something placed on the map. Exactly one of <see cref="Player"/> / <see cref="Container"/> is set for
/// editable markers; other buildings carry only their actor. Conveyor belts also carry their
/// <see cref="Path"/> in world coordinates so they can be drawn as lines.
/// </summary>
public sealed record MapMarker(MapMarkerKind Kind, ActorObject Actor, Vector3 Position, string Label)
{
    public Player? Player { get; init; }
    public Container? Container { get; init; }
    public IReadOnlyList<Vector3>? Path { get; init; }
    /// <summary>Bounding box of <see cref="Path"/> on the ground plane (min X/Y, max X/Y).</summary>
    public (Vector2 Min, Vector2 Max) PathBounds { get; init; }
}

/// <summary>A power line drawn between the buildings at its two ends.</summary>
public sealed record MapLine(Vector3 From, Vector3 To, ActorObject Line, ActorObject FromBuilding, ActorObject ToBuilding);

/// <summary>Builds map markers for players, inventories (containers, vehicles, machines...) and other buildings.</summary>
public sealed class MapService(SaveEditor editor)
{
    /// <summary>Power lines whose both ends resolve to placed buildings.</summary>
    public IReadOnlyList<MapLine> PowerLines =>
        editor.Power.Lines
            .Where(l => l.SourceBuilding is { } s && l.TargetBuilding is { } t && HasPosition(s) && HasPosition(t))
            .Select(l => new MapLine(l.SourceBuilding!.Position, l.TargetBuilding!.Position, l.Line, l.SourceBuilding!, l.TargetBuilding!))
            .ToList();

    public IReadOnlyList<MapMarker> Markers
    {
        get
        {
            var markers = new List<MapMarker>();
            var placed = new HashSet<ActorObject>();

            foreach (var player in editor.Players.All)
            {
                if (player.Pawn is null)
                    continue;
                markers.Add(new MapMarker(MapMarkerKind.Player, player.Pawn, player.Pawn.Position, player.Name) { Player = player });
                placed.Add(player.Pawn);
            }

            foreach (var container in editor.Containers.All)
            {
                if (!HasPosition(container.Owner))
                    continue;
                markers.Add(new MapMarker(KindOf(container.Category), container.Owner, container.Position, container.TypeName)
                {
                    Container = container,
                });
                placed.Add(container.Owner);
            }

            foreach (var actor in editor.Save.Body.AllObjects.OfType<ActorObject>())
            {
                if (placed.Contains(actor) || !HasPosition(actor) || !actor.ClassName.StartsWith("Build_", StringComparison.OrdinalIgnoreCase))
                    continue;
                string label = ContainerService.FriendlyClassName(actor.ClassName);
                if (ConveyorPath.Of(actor) is { Count: >= 2 } path)
                {
                    markers.Add(new MapMarker(MapMarkerKind.Conveyor, actor, actor.Position, label)
                    {
                        Path = path,
                        PathBounds = (new Vector2(path.Min(p => p.X), path.Min(p => p.Y)), new Vector2(path.Max(p => p.X), path.Max(p => p.Y))),
                    });
                }
                else
                {
                    markers.Add(new MapMarker(MapMarkerKind.Building, actor, actor.Position, label));
                }
            }

            return markers.OrderBy(m => m.Kind).ToList();
        }
    }

    /// <summary>Actors without a stored transform report the origin; they cannot be placed.</summary>
    private static bool HasPosition(ActorObject actor) => actor.Position != Vector3.Zero;

    private static MapMarkerKind KindOf(ContainerCategory category) => category switch
    {
        ContainerCategory.Storage => MapMarkerKind.Storage,
        ContainerCategory.Vehicles => MapMarkerKind.Vehicle,
        ContainerCategory.Machines => MapMarkerKind.Machine,
        ContainerCategory.Logistics => MapMarkerKind.Logistics,
        _ => MapMarkerKind.World,
    };
}
