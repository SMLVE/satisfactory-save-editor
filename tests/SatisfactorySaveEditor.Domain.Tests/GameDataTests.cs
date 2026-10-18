namespace SatisfactorySaveEditor.Domain.Tests;

public class GameDataTests
{
    [Fact]
    public void Items_UseCachedStackSize_AndHarvestMissingPaths()
    {
        var data = TestEditors.GameData;

        var plate = data.FindItem("Desc_IronPlate_C")!;
        Assert.Equal("Iron Plate", plate.DisplayName);
        Assert.Equal(200, plate.StackSize);
        Assert.Equal("/Game/FactoryGame/Resource/Parts/IronPlate/Desc_IronPlate.Desc_IronPlate_C", plate.ClassPath);
        Assert.Equal(50, data.FindItem("/Game/FactoryGame/Resource/Parts/Motor/Desc_Motor.Desc_Motor_C")!.StackSize);
    }

    [Fact]
    public void BuildingDescriptors_AreNotItems()
    {
        Assert.Null(TestEditors.GameData.FindItem("Desc_SmelterMk1_C"));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"NativeClass": "x"}""")]
    [InlineData("""[{"Classes": []}]""")]
    [InlineData("""[{"NativeClass": 1, "Classes": []}]""")]
    public void Parse_NotDocsJson_ThrowsGameDataException(string json)
    {
        Assert.Throws<Domain.GameData.GameDataException>(() => Domain.GameData.GameData.Parse(json));
    }

    [Fact]
    public void Schematics_ParseUnlocks()
    {
        var milestone = TestEditors.GameData.FindSchematic("Schematic_3-4_C")!;
        Assert.True(milestone.IsMilestone);
        Assert.Equal(3, milestone.TechTier);
        var recipes = milestone.Unlocks.Single(u => u.Kind == "BP_UnlockRecipe_C");
        Assert.Equal(
            ["/Game/FactoryGame/Recipes/Smelter/Recipe_IngotSteel.Recipe_IngotSteel_C", "/Game/FactoryGame/Recipes/Constructor/Recipe_SteelBeam.Recipe_SteelBeam_C"],
            recipes.Paths);

        var upgrades = TestEditors.GameData.FindSchematic("Schematic_Test_Upgrades_C")!;
        Assert.Equal(3, upgrades.Unlocks.Single(u => u.Kind == "BP_UnlockInventorySlot_C").Amount);
    }
}
