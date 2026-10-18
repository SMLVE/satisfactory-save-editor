using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Core.Tests;

public class PropertyTests
{
    [Theory]
    [MemberData(nameof(SampleSaves.All), MemberType = typeof(SampleSaves))]
    public void AllObjectsAndValues_AreFullyParsed(string sample)
    {
        var save = SaveFile.Load(SampleSaves.Bytes(sample));
        var objects = save.Body.AllObjects.ToList();

        var unparsed = objects.Where(o => !o.IsParsed && o.RawData.Length > 0).Select(o => $"{o.ClassName}: {o.ParseError}").ToList();
        Assert.True(unparsed.Count == 0, string.Join("\n", unparsed.Take(10)));

        var values = objects.SelectMany(o => PropertyWalker.Descendants(o.Properties)).ToList();
        Assert.DoesNotContain(values, v => v is RawValue or RawStruct);
        Assert.DoesNotContain(values, v => v is InventoryItemStruct { HasState: not 0, State: null });
    }

    [Fact]
    public void PlayerInventory_HasExpectedStacks()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample2.sav"));

        var stacks = PlayerInventoryStacks(save);

        Assert.Equal(50, stacks.Elements.Count);
        var (item, count) = Stack(stacks, 0);
        Assert.EndsWith("Desc_IronPlate_C", item.ItemClass, StringComparison.Ordinal);
        Assert.Equal(10, count.Value);
    }

    [Fact]
    public void EditingFixedSizeValue_ChangesOnlyThoseBytes()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample2.sav"));
        var before = save.SerializeBody();

        var (_, count) = Stack(PlayerInventoryStacks(save), 0);
        count.Value = 500;
        var after = save.SerializeBody();

        Assert.Equal(before.Length, after.Length);
        int differing = before.Zip(after).Count(p => p.First != p.Second);
        Assert.InRange(differing, 1, 4);

        var reloaded = SaveFile.Load(save.ToBytes());
        Assert.Equal(500, Stack(PlayerInventoryStacks(reloaded), 0).Count.Value);
    }

    [Fact]
    public void EditingVariableSizeValues_RecomputesSizes()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample2.sav"));
        var stacks = PlayerInventoryStacks(save);

        // Put a longer item class into an empty slot and append a new array element.
        var (emptyItem, emptyCount) = Stack(stacks, 2);
        emptyItem.ItemClass = "/Game/FactoryGame/Resource/Parts/Motor/Desc_Motor.Desc_Motor_C";
        emptyCount.Value = 25;
        var sizes = save.Body.PersistentLevel.Objects
            .First(o => o.InstanceName!.EndsWith("Char_Player_C_0.inventory", StringComparison.Ordinal))
            .Properties.Get<ArrayValue>("mArbitrarySlotSizes")!;
        sizes.Elements.Add(new IntValue(7));

        var reloaded = SaveFile.Load(save.ToBytes());

        var reloadedStacks = PlayerInventoryStacks(reloaded);
        var (item, count) = Stack(reloadedStacks, 2);
        Assert.Equal("/Game/FactoryGame/Resource/Parts/Motor/Desc_Motor.Desc_Motor_C", item.ItemClass);
        Assert.Equal(25, count.Value);
        var reloadedSizes = reloaded.Body.PersistentLevel.Objects
            .First(o => o.InstanceName!.EndsWith("Char_Player_C_0.inventory", StringComparison.Ordinal))
            .Properties.Get<ArrayValue>("mArbitrarySlotSizes")!;
        Assert.Equal(51, reloadedSizes.Elements.Count);
        Assert.All(reloaded.Body.AllObjects, o => Assert.True(o.IsParsed || o.RawData.Length == 0, o.ParseError));
        Assert.Equal(save.SerializeBody(), reloaded.SerializeBody());
    }

    private static ArrayValue PlayerInventoryStacks(SaveFile save) =>
        save.Body.PersistentLevel.Objects
            .First(o => o.InstanceName!.EndsWith("Char_Player_C_0.inventory", StringComparison.Ordinal))
            .Properties.Get<ArrayValue>("mInventoryStacks")!;

    private static (InventoryItemStruct Item, IntValue Count) Stack(ArrayValue stacks, int index)
    {
        var stack = (PropertyListStruct)stacks.Elements[index];
        return (stack.Properties.Get<InventoryItemStruct>("Item")!, stack.Properties.Get<IntValue>("NumItems")!);
    }
}
