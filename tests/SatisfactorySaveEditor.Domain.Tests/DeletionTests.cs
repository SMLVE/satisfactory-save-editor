using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;
using SatisfactorySaveEditor.Core.Trailing;

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
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(container.InstanceName!, StringComparison.Ordinal));
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
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(constructor.InstanceName!, StringComparison.Ordinal));
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
        Assert.DoesNotContain(AllReferences(reloaded), r => r.StartsWith(truck.InstanceName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Crate_IsDeleted()
    {
        var editor = TestEditors.Open("sample2.sav");
        var crate = editor.Save.Body.AllObjects.OfType<ActorObject>().First(a => a.ClassName == "BP_Crate_C");

        editor.Deletion.Execute(editor.Deletion.Plan(crate));

        Assert.Null(TestEditors.Reload(editor).Find(crate.InstanceName));
    }

    [Fact]
    public void Belt_RemovesItsChain_AndUnlinksTheOtherBeltsFromIt()
    {
        var editor = TestEditors.Open("sample2.sav");
        // A belt in a chain of several belts.
        var chainActor = editor.Save.Body.AllObjects.OfType<ActorObject>()
            .First(a => a.Trailing is ConveyorChainTrailing { Segments.Count: >= 3 });
        var chain = (ConveyorChainTrailing)chainActor.Trailing!;
        var belt = (ActorObject)editor.Find(chain.Segments[1].Belt.PathName)!;
        var otherBelts = chain.Segments.Where(s => s.Belt.PathName != belt.InstanceName).Select(s => s.Belt.PathName!).ToList();

        var plan = editor.Deletion.Plan(belt);

        Assert.True(plan.CanDelete, plan.BlockedReason);
        Assert.Contains(chainActor, plan.AlsoDeleted);
        Assert.All(otherBelts, name => Assert.Contains(plan.ReferencesToClear, r => r.Owner.InstanceName == name));
        editor.Deletion.Execute(plan);

        var reloaded = TestEditors.Reload(editor);
        Assert.Null(reloaded.Find(belt.InstanceName));
        Assert.Null(reloaded.Find(chainActor.InstanceName));
        Assert.All(otherBelts, name =>
            Assert.True(reloaded.Find(name)!.Properties.Get<ObjectValue>("mConveyorChainActor")!.Value.IsEmpty));
        Assert.DoesNotContain(AllReferences(reloaded), r => r == belt.InstanceName || r == chainActor.InstanceName);
    }

    [Theory]
    [InlineData("Char_Player_C", "Players")]
    [InlineData("BP_DropPod_C", "part of the map")]
    public void UnsafeDeletions_AreRefused(string className, string reasonFragment)
    {
        var editor = TestEditors.Open("sample2.sav");
        var actor = editor.Save.Body.AllObjects.OfType<ActorObject>().First(a => a.ClassName == className);

        var plan = editor.Deletion.Plan(actor);

        Assert.False(plan.CanDelete);
        Assert.Contains(reasonFragment, plan.BlockedReason, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => editor.Deletion.Execute(plan));
    }

    private static ActorObject Actor(SaveEditor editor, string shortName) =>
        (ActorObject)editor.Find($"Persistent_Level:PersistentLevel.{shortName}")!;

    private static IEnumerable<string> AllReferences(SaveEditor editor) =>
        editor.Save.Body.AllObjects.Where(o => o.IsParsed)
            .SelectMany(o => PropertyWalker.Descendants(o.Properties))
            .OfType<ObjectValue>().Select(v => v.Value.PathName).OfType<string>();
}
