using SatisfactorySaveEditor.Domain.Progression;

namespace SatisfactorySaveEditor.Domain.Tests;

public class SinkAndDepotTests
{
    [Fact]
    public void Sink_ReadsPointsLevelsAndCoupons()
    {
        var sink = TestEditors.Open("sample3.sav").Sink;

        Assert.Equal(6_740_200, sink.PointsOf(SinkTrack.Standard));
        Assert.Equal(0, sink.PointsOf(SinkTrack.Dna));
        Assert.Equal(91, sink.LevelOf(SinkTrack.Standard));
        Assert.Equal(91, sink.CouponsToPrint);
    }

    [Fact]
    public void Sink_EditsPersist()
    {
        var editor = TestEditors.Open("sample3.sav");

        editor.Sink.CouponsToPrint = 500;
        editor.Sink.SetPoints(SinkTrack.Dna, 12_345);

        var reloaded = TestEditors.Reload(editor).Sink;
        Assert.Equal(500, reloaded.CouponsToPrint);
        Assert.Equal(12_345, reloaded.PointsOf(SinkTrack.Dna));
        Assert.Equal(6_740_200, reloaded.PointsOf(SinkTrack.Standard));
    }

    [Fact]
    public void Sink_IsCreatedWhenAbsent()
    {
        var editor = TestEditors.Open("sample2.sav"); // never used the sink: no properties
        Assert.Equal(0, editor.Sink.CouponsToPrint);
        Assert.Equal(0, editor.Sink.PointsOf(SinkTrack.Standard));

        editor.Sink.CouponsToPrint = 10;
        editor.Sink.SetPoints(SinkTrack.Standard, 1_000_000);

        var reloaded = TestEditors.Reload(editor).Sink;
        Assert.Equal(10, reloaded.CouponsToPrint);
        Assert.Equal(1_000_000, reloaded.PointsOf(SinkTrack.Standard));
        Assert.Equal(0, reloaded.PointsOf(SinkTrack.Dna));
    }

    [Fact]
    public void Depot_ReadsStoredItems()
    {
        var items = TestEditors.Open("sample3.sav").Depot.Items;

        Assert.Equal(3, items.Count);
        var concrete = items.Single(i => i.ItemClass.EndsWith("Desc_Cement_C"));
        Assert.Equal(58, concrete.Amount);
        Assert.Equal("Concrete", concrete.DisplayName);
    }

    [Fact]
    public void Depot_SetAddAndRemove_Persist()
    {
        var editor = TestEditors.Open("sample3.sav");

        editor.Depot.SetAmount("Desc_Cement_C", 500);   // change
        editor.Depot.SetAmount("Desc_Motor_C", 25);     // add
        editor.Depot.Remove("/Game/FactoryGame/Resource/Parts/IronRod/Desc_IronRod.Desc_IronRod_C"); // remove

        var items = TestEditors.Reload(editor).Depot.Items;
        Assert.Equal(500, items.Single(i => i.ItemClass.EndsWith("Desc_Cement_C")).Amount);
        Assert.Equal(25, items.Single(i => i.ItemClass.EndsWith("Desc_Motor_C")).Amount);
        Assert.DoesNotContain(items, i => i.ItemClass.EndsWith("Desc_IronRod_C"));
    }

    [Fact]
    public void Depot_IsCreatedWhenAbsent()
    {
        var editor = TestEditors.Open("sample2.sav"); // depot never used
        Assert.Empty(editor.Depot.Items);

        editor.Depot.SetAmount("Desc_IronPlate_C", 1000);

        var item = Assert.Single(TestEditors.Reload(editor).Depot.Items);
        Assert.Equal(1000, item.Amount);
        Assert.Equal("Iron Plate", item.DisplayName);
    }
}
