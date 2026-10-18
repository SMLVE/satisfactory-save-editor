using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Tests;

public class DeletionTests
{
    [Fact]
    public void StorageContainer_IsRemovedWithComponents_AndBeltEndsAreUnhooked()
    {
        var editor = TestEditors.Open("sample2.sav");
        var container = Actor(editor, "Build_StorageContainerMk2_C_148");
        int objectsBefore = editor.Save.Body.AllObjects.Count();

        var plan = editor.Deletion.Plan(container);

        Assert.True(plan.CanDelete, plan.BlockedReason);
        Assert.Equal(8, plan.ComponentCount);
        Assert.Empty(plan.AlsoDeleted);
        Assert.Equal(2, plan.ReferencesToClear.Count); // the two conveyor connections pointing at its input/output
        editor.Deletion.Execute(plan);

        var reloaded = TestEditors.Reload(editor);
        Assert.Equal(objectsBefore - 9, reloaded.Save.Body.AllObjects.Count());
        Assert.Null(reloaded.Find(container.InstanceName));
        Assert.DoesNotContain(reloaded.Save.Body.AllObjects.OfType<ComponentObject>(), c => c.ParentActorName == container.InstanceName);
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(container.InstanceName!));
    }

    [Fact]
    public void Machine_AlsoDeletesAttachedPowerLine_AndLeavesCircuit()
    {
        var editor = TestEditors.Open("sample2.sav");
        var constructor = Actor(editor, "Build_ConstructorMk1_C_182");

        var plan = editor.Deletion.Plan(constructor);

        Assert.True(plan.CanDelete, plan.BlockedReason);
        Assert.Contains(plan.AlsoDeleted, a => a.ClassName == "Build_PowerLine_C");
        Assert.Contains(plan.ReferencesToClear, r => r.Owner.ClassName == "FGPowerCircuit");
        editor.Deletion.Execute(plan);

        var reloaded = TestEditors.Reload(editor);
        Assert.Null(reloaded.Find(constructor.InstanceName));
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(constructor.InstanceName!));
    }

    [Fact]
    public void Vehicle_IsDeletedWithItsIdentifier()
    {
        var editor = TestEditors.Open("sample2.sav");
        var truck = Actor(editor, "BP_Truck_C_1");

        var plan = editor.Deletion.Plan(truck);

        Assert.True(plan.CanDelete, plan.BlockedReason);
        Assert.Contains(plan.ObjectsToRemove, o => o.ClassName == "FGWheeledVehicleIdentifier");
        editor.Deletion.Execute(plan);
        var reloaded = TestEditors.Reload(editor);
        Assert.Null(reloaded.Find(truck.InstanceName));
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(truck.InstanceName!));
    }

    [Fact]
    public void Crate_IsDeleted()
    {
        var editor = TestEditors.Open("sample2.sav");
        var crate = editor.Save.Body.AllObjects.OfType<ActorObject>().First(a => a.ClassName == "BP_Crate_C");

        editor.Deletion.Execute(editor.Deletion.Plan(crate));

        Assert.Null(TestEditors.Reload(editor).Find(crate.InstanceName));
    }

    [Theory]
    [InlineData("Char_Player_C", "Players")]
    [InlineData("BP_DropPod_C", "part of the map")]
    [InlineData("Build_ConveyorBeltMk4_C", "can't edit")]
    public void UnsafeDeletions_AreRefused(string className, string reasonFragment)
    {
        var editor = TestEditors.Open("sample2.sav");
        var actor = editor.Save.Body.AllObjects.OfType<ActorObject>().First(a => a.ClassName == className);

        var plan = editor.Deletion.Plan(actor);

        Assert.False(plan.CanDelete);
        Assert.Contains(reasonFragment, plan.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => editor.Deletion.Execute(plan));
    }

    private static ActorObject Actor(SaveEditor editor, string shortName) =>
        (ActorObject)editor.Find($"Persistent_Level:PersistentLevel.{shortName}")!;

    private static IEnumerable<string> AllReferences(SaveEditor editor) =>
        editor.Save.Body.AllObjects.Where(o => o.IsParsed)
            .SelectMany(o => PropertyWalker.Descendants(o.Properties))
            .OfType<ObjectValue>().Select(v => v.Value.PathName).OfType<string>();
}
