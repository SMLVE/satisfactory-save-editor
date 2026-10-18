using System.Numerics;
using SatisfactorySaveEditor.Domain.Map;

namespace SatisfactorySaveEditor.Domain.Tests;

public class MapTests
{
    [Fact]
    public void Markers_CoverPlayersContainersAndBuildings()
    {
        var markers = TestEditors.Open("sample2.sav").Map.Markers;

        var players = markers.Where(m => m.Kind == MapMarkerKind.Player).ToList();
        Assert.Equal(2, players.Count);
        Assert.All(players, p => Assert.NotNull(p.Player));
        Assert.Equal(players[0].Player!.Position, players[0].Position);

        Assert.Equal(139, markers.Count(m => m.Actor.ClassName == "Build_StorageContainerMk2_C"));
        Assert.All(markers.Where(m => m.Kind == MapMarkerKind.Storage), m => Assert.NotNull(m.Container));
        Assert.Contains(markers, m => m.Kind == MapMarkerKind.Vehicle && m.Label == "Truck");
        Assert.Contains(markers, m => m.Kind == MapMarkerKind.Building && m.Actor.ClassName == "Build_ConveyorPole_C");
    }

    [Fact]
    public void Belts_AreConveyorMarkersWithWorldPaths()
    {
        var markers = TestEditors.Open("sample2.sav").Map.Markers;

        var belts = markers.Where(m => m.Actor.ClassName.StartsWith("Build_ConveyorBelt", StringComparison.Ordinal)).ToList();
        Assert.Equal(5062, belts.Count);
        Assert.All(belts, b =>
        {
            Assert.Equal(MapMarkerKind.Conveyor, b.Kind);
            Assert.True(b.Path!.Count >= 2);
            // Paths start at the belt (usually exactly at its origin; a few splines start slightly offset).
            Assert.True(Vector3.Distance(b.Actor.Position, b.Path[0]) < 5_000, $"{b.Actor.InstanceName} path starts far away");
        });

        // The belt inspected earlier: 4 spline points ending 2751 cm along X (rotation is identity).
        var belt = belts.Single(b => b.Actor.InstanceName!.EndsWith(".Build_ConveyorBeltMk4_C_4306", StringComparison.Ordinal));
        var end = belt.Path![^1];
        Assert.Equal(belt.Actor.Position.X + 2751f, end.X, 0.5f);
        Assert.Equal(belt.Actor.Position.Y - 336.015625f, end.Y, 0.5f);
        Assert.True(belt.Path.Count > 4); // the curved piece was sampled
    }

    [Fact]
    public void Markers_SkipActorsWithoutPositionAndAppearOnce()
    {
        var markers = TestEditors.Open("sample2.sav").Map.Markers;

        Assert.DoesNotContain(markers, m => m.Position == Vector3.Zero);
        Assert.Equal(markers.Count, markers.Select(m => m.Actor).Distinct().Count());
    }

    [Fact]
    public void Markers_AreOrderedSoPlayersDrawLast()
    {
        var markers = TestEditors.Open("sample2.sav").Map.Markers;

        Assert.Equal(MapMarkerKind.Player, markers[^1].Kind);
        Assert.True(markers.Select(m => (int)m.Kind).SequenceEqual(markers.Select(m => (int)m.Kind).Order()));
    }
}
