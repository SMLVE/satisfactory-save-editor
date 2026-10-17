using System.Numerics;
using SatisfactorySaveEditor.Domain.Players;

namespace SatisfactorySaveEditor.Domain.Tests;

public class PlayerTests
{
    [Fact]
    public void Sample2_HasTwoPlayersWithIdentities()
    {
        var players = TestEditors.Open("sample2.sav").Players.All;

        Assert.Equal(2, players.Count);
        Assert.Equal("Epic", players[0].Identity!.PlatformName);
        Assert.Equal("Steam", players[1].Identity!.PlatformName);
        Assert.Equal("76561197960265728", players[1].Identity!.Id);
        Assert.Equal(33.31185f, players[0].Health, 0.0001f);
    }

    [Fact]
    public void Health_DefaultsToMax_AndCanBeSetWhenAbsent()
    {
        var editor = TestEditors.Open("sample.sav");
        var player = editor.Players.All.Single();
        Assert.Equal(Player.MaxHealth, player.Health);

        player.Health = 42.5f;

        var reloaded = TestEditors.Reload(editor).Players.All.Single();
        Assert.Equal(42.5f, reloaded.Health);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(100.5f)]
    public void Health_RejectsOutOfRangeValues(float health)
    {
        var player = TestEditors.Open("sample.sav").Players.All.Single();
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Health = health);
    }

    [Fact]
    public void Position_Persists()
    {
        var editor = TestEditors.Open("sample2.sav");
        var target = new Vector3(1000f, -2000f, 3000f);

        editor.Players.All[1].Position = target;

        Assert.Equal(target, TestEditors.Reload(editor).Players.All[1].Position);
    }
}
