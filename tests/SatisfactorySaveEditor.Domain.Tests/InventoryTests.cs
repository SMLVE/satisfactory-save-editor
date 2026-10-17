using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Tests;

public class InventoryTests
{
    [Fact]
    public void Slots_ResolveDisplayNames()
    {
        var inventory = TestEditors.Open("sample2.sav").Players.All[0].Inventory;

        Assert.Equal(50, inventory.Size);
        Assert.Equal("Iron Plate", inventory.GetSlot(0).DisplayName);
        Assert.Equal(10, inventory.GetSlot(0).Count);
        Assert.True(inventory.GetSlot(2).IsEmpty);
    }

    [Fact]
    public void SetSlot_ByClassName_Persists()
    {
        var editor = TestEditors.Open("sample2.sav");

        editor.Players.All[0].Inventory.SetSlot(2, "Desc_Motor_C", 50);

        var slot = TestEditors.Reload(editor).Players.All[0].Inventory.GetSlot(2);
        Assert.Equal("/Game/FactoryGame/Resource/Parts/Motor/Desc_Motor.Desc_Motor_C", slot.ItemClass);
        Assert.Equal(50, slot.Count);
    }

    [Fact]
    public void SetSlot_EnforcesStackSizeUnlessOverstackAllowed()
    {
        var inventory = TestEditors.Open("sample2.sav").Players.All[0].Inventory;

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.SetSlot(2, "Desc_Motor_C", 51));
        inventory.SetSlot(2, "Desc_Motor_C", 51, allowOverstack: true);
        Assert.Equal(51, inventory.GetSlot(2).Count);
    }

    [Fact]
    public void SetSlot_RejectsUnknownItems()
    {
        var inventory = TestEditors.Open("sample2.sav").Players.All[0].Inventory;
        Assert.Throws<ArgumentException>(() => inventory.SetSlot(2, "Desc_NotAThing_C", 1));
    }

    [Fact]
    public void ClearSlot_EmptiesIt()
    {
        var editor = TestEditors.Open("sample2.sav");

        editor.Players.All[0].Inventory.ClearSlot(0);

        Assert.True(TestEditors.Reload(editor).Players.All[0].Inventory.GetSlot(0).IsEmpty);
    }

    [Fact]
    public void FillStacks_TopsUpToStackSize()
    {
        var editor = TestEditors.Open("sample2.sav");
        var inventory = editor.Players.All[0].Inventory;

        int changed = inventory.FillStacks();

        Assert.Equal(2, changed);
        var reloaded = TestEditors.Reload(editor).Players.All[0].Inventory;
        Assert.Equal(200, reloaded.GetSlot(0).Count); // Iron Plate, was 10
        Assert.Equal(100, reloaded.GetSlot(1).Count); // Concrete, was 99
    }

    [Fact]
    public void Resize_KeepsPerSlotArraysInSync()
    {
        var editor = TestEditors.Open("sample.sav");
        var inventory = editor.Players.All.Single().Inventory;
        int diffBefore = inventory.Component.Properties.Get<IntValue>("mAdjustedSizeDiff")!.Value;

        inventory.Resize(30);

        var reloaded = TestEditors.Reload(editor).Players.All.Single().Inventory;
        Assert.Equal(30, reloaded.Size);
        Assert.Equal(30, reloaded.Component.Properties.Get<ArrayValue>("mArbitrarySlotSizes")!.Elements.Count);
        Assert.Equal(30, reloaded.Component.Properties.Get<ArrayValue>("mAllowedItemDescriptors")!.Elements.Count);
        Assert.Equal(diffBefore + 3, reloaded.Component.Properties.Get<IntValue>("mAdjustedSizeDiff")!.Value);
        Assert.True(reloaded.GetSlot(29).IsEmpty);
    }

    [Fact]
    public void Resize_RefusesToDropItems()
    {
        var inventory = TestEditors.Open("sample2.sav").Players.All[0].Inventory;
        Assert.Throws<InvalidOperationException>(() => inventory.Resize(1));
    }
}
