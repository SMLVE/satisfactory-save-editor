using System.Numerics;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Domain.Inventory;
using SatisfactorySaveEditor.Domain.Players;

namespace SatisfactorySaveEditor.Domain.Map;

/// <summary>Marker layers, drawn in this order (later kinds on top).</summary>
public enum MapMarkerKind { Building, Logistics, Machine, World, Storage, Vehicle, Player }

/// <summary>
/// Something placed on the map. Exactly one of <see cref="Player"/> / <see cref="Container"/> is set for
/// editable markers; other buildings carry only their actor.
/// </summary>
public sealed record MapMarker(MapMarkerKind Kind, ActorObject Actor, Vector3 Position, string Label)
{
    public Player? Player { get; init; }
    public Container? Container { get; init; }
}

/// <summary>Builds map markers for players, inventories (containers, vehicles, machines...) and other buildings.</summary>
public sealed class MapService(SaveEditor editor)
{
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
                markers.Add(new MapMarker(MapMarkerKind.Building, actor, actor.Position, ContainerService.FriendlyClassName(actor.ClassName)));
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
