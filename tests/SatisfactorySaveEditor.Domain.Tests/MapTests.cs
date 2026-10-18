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
        Assert.Contains(markers, m => m.Kind == MapMarkerKind.Building && m.Actor.ClassName == "Build_ConveyorBeltMk4_C");
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
