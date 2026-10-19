using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Tests;

public class ProgressionTests
{
    [Fact]
    public void Unlock_Milestone_AddsSchematicAndRecipes()
    {
        var editor = TestEditors.Open("sample.sav");
        int recipesBefore = editor.Progression.AvailableRecipes.Count;

        var result = editor.Progression.Unlock("Schematic_3-4_C");

        Assert.False(result.AlreadyPurchased);
        Assert.Equal(["2 recipes"], result.Applied);
        Assert.Empty(result.Skipped);
        var reloaded = TestEditors.Reload(editor).Progression;
        Assert.True(reloaded.IsPurchased("Schematic_3-4_C"));
        Assert.Contains("/Game/FactoryGame/Recipes/Smelter/Recipe_IngotSteel.Recipe_IngotSteel_C", reloaded.AvailableRecipes);
        Assert.Equal(recipesBefore + 2, reloaded.AvailableRecipes.Count);
    }

    [Fact]
    public void Unlock_AlreadyPurchased_DoesNothing()
    {
        var editor = TestEditors.Open("sample.sav");

        var result = editor.Progression.Unlock("Schematic_Tutorial1_C");

        Assert.True(result.AlreadyPurchased);
        Assert.Equal(7, editor.Progression.PurchasedSchematics.Count);
    }

    [Fact]
    public void Unlock_SlotsAndFlags_ResizesInventoriesAndReportsSkipped()
    {
        var editor = TestEditors.Open("sample.sav");
        Assert.Equal(27, editor.Progression.InventorySlots);
        Assert.Equal(2, editor.Progression.ArmEquipmentSlots);

        var result = editor.Progression.Unlock("Schematic_Test_Upgrades_C");

        Assert.Equal(["BP_UnlockGiveItem_C"], result.Skipped);
        var reloaded = TestEditors.Reload(editor);
        Assert.Equal(30, reloaded.Progression.InventorySlots);
        Assert.Equal(3, reloaded.Progression.ArmEquipmentSlots);
        var player = reloaded.Players.All.Single();
        Assert.Equal(30, player.Inventory.Size);
        Assert.Equal(3, player.EquipmentSlots["Hands"].Size);
        var unlockSubsystem = reloaded.Single("BP_UnlockSubsystem_C");
        Assert.True(unlockSubsystem.Properties.Get<BoolValue>("mIsMapUnlocked")!.Value);
    }

    [Fact]
    public void Unlock_EffectThatCannotApply_LeavesSaveUnchanged()
    {
        var editor = TestEditors.Open("sample.sav");
        // A hand-slot inventory with no slots can't be grown. The upgrade schematic first grows the main
        // inventory, then the hand slots: neither the purchase nor the earlier effect may be written.
        editor.Players.All.Single().EquipmentSlots["Hands"].Component.Properties
            .Get<ArrayValue>("mInventoryStacks")!.Elements.Clear();
        var before = editor.Save.SerializeBody();

        Assert.Throws<InvalidOperationException>(() => editor.Progression.Unlock("Schematic_Test_Upgrades_C"));

        Assert.Equal(before, editor.Save.SerializeBody());
        Assert.False(editor.Progression.IsPurchased("Schematic_Test_Upgrades_C"));
        Assert.Equal(27, editor.Players.All.Single().Inventory.Size);
    }

    [Fact]
    public void Unlock_PlayerWithoutInventory_LeavesSaveUnchanged()
    {
        var editor = TestEditors.Open("sample.sav");
        editor.Players.All.Single().Pawn!.Properties.Get<ObjectValue>("mInventory")!.Value = ObjectReference.Empty;
        var before = editor.Save.SerializeBody();

        Assert.Throws<InvalidOperationException>(() => editor.Progression.Unlock("Schematic_Test_Upgrades_C"));

        Assert.Equal(before, editor.Save.SerializeBody());
        Assert.False(editor.Progression.IsPurchased("Schematic_Test_Upgrades_C"));
    }

    [Fact]
    public void UnlockWhere_UnlocksMatchingSchematicsOnce()
    {
        var editor = TestEditors.Open("sample.sav");

        var results = editor.Progression.UnlockWhere(s => s.IsMilestone);

        Assert.Equal(["Schematic_Test_Upgrades_C", "Schematic_3-4_C"], results.Select(r => r.Schematic.ClassName));
        Assert.Empty(editor.Progression.UnlockWhere(s => s.IsMilestone));
    }

    [Fact]
    public void Unlock_UnknownSchematic_Throws()
    {
        var editor = TestEditors.Open("sample.sav");
        Assert.Throws<ArgumentException>(() => editor.Progression.Unlock("Schematic_Nope_C"));
    }
}
