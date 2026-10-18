using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.Domain.Tests;

public class ContainerTests
{
    [Fact]
    public void Containers_AreGroupedAndCategorized()
    {
        var containers = TestEditors.Open("sample2.sav").Containers.All;

        Assert.DoesNotContain(containers, c => c.Owner.ClassName == "Char_Player_C");
        Assert.Equal(139, containers.Count(c => c.Owner.ClassName == "Build_StorageContainerMk2_C"));
        Assert.All(containers.Where(c => c.Owner.ClassName == "Build_StorageContainerMk2_C"),
            c => Assert.Equal(ContainerCategory.Storage, c.Category));

        var truck = containers.First(c => c.Owner.ClassName == "BP_Truck_C");
        Assert.Equal(ContainerCategory.Vehicles, truck.Category);
        Assert.Equal(["Storage", "Fuel"], truck.Inventories.Select(i => i.Name));

        var smelter = containers.First(c => c.Owner.ClassName == "Build_SmelterMk1_C");
        Assert.Equal(ContainerCategory.Machines, smelter.Category);
        Assert.Equal(ContainerCategory.Logistics, containers.First(c => c.Owner.ClassName.Contains("Splitter", StringComparison.Ordinal)).Category);
        Assert.Equal(ContainerCategory.World, containers.First(c => c.Owner.ClassName == "BP_DropPod_C").Category);
    }

    [Theory]
    [InlineData("Build_StorageContainerMk2_C", "Storage Container Mk2")]
    [InlineData("BP_Truck_C", "Truck")]
    [InlineData("Build_ConveyorAttachmentSplitterSmart_C", "Conveyor Attachment Splitter Smart")]
    [InlineData("BUILD_Potty_mk1_C", "Potty mk1")]
    public void FriendlyClassName_IsReadable(string className, string expected) =>
        Assert.Equal(expected, ContainerService.FriendlyClassName(className));

    [Fact]
    public void ContainerSlotEdit_Persists()
    {
        var editor = TestEditors.Open("sample2.sav");
        var container = editor.Containers.All.First(c => c.Owner.ClassName == "Build_StorageContainerMk2_C"
                                                         && c.UsedSlots < c.TotalSlots);
        var storage = container.Inventories.Single(i => i.Name == "Storage").Inventory;
        int empty = storage.Slots.First(s => s.IsEmpty).Index;

        storage.SetSlot(empty, "Desc_Motor_C", 50);

        var reloaded = TestEditors.Reload(editor).Containers.All.First(c => c.InstanceName == container.InstanceName);
        var slot = reloaded.Inventories.Single(i => i.Name == "Storage").Inventory.GetSlot(empty);
        Assert.Equal(50, slot.Count);
        Assert.Equal("Motor", slot.DisplayName);
    }
}
