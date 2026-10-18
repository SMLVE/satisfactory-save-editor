using System.Numerics;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Structures;

namespace SatisfactorySaveEditor.Domain.Tests;

public class SelectionEditTests
{
    [Fact]
    public void Structures_CanBeDeleted()
    {
        var editor = TestEditors.Open("sample2.sav");
        var all = editor.Structures.All;
        var walls = all.Where(s => s.Kind == StructureKind.Wall).Take(25).ToList();

        int removed = editor.Structures.Delete(walls);

        Assert.Equal(25, removed);
        var reloaded = TestEditors.Reload(editor).Structures.All;
        Assert.Equal(all.Count - 25, reloaded.Count);
        Assert.DoesNotContain(reloaded, s => walls.Any(w => w.Position == s.Position && w.ClassName == s.ClassName && w.Yaw == s.Yaw));
    }

    [Fact]
    public void DeletingAllOfAClass_DropsTheEmptyClass()
    {
        var editor = TestEditors.Open("sample2.sav");
        var stairs = editor.Structures.All.Where(s => s.ClassName == "Build_Stairs_Left_01_C").ToList();

        editor.Structures.Delete(stairs);

        var data = (LightweightBuildablesTrailing)TestEditors.Reload(editor).Single("FGLightweightBuildableSubsystem").Trailing!;
        Assert.Equal(12, data.Classes.Count);
        Assert.DoesNotContain(data.Classes, c => c.ClassName == "Build_Stairs_Left_01_C");
    }

    [Fact]
    public void Structures_CanBeMoved()
    {
        var editor = TestEditors.Open("sample2.sav");
        var foundations = editor.Structures.All.Where(s => s.Kind == StructureKind.Foundation).Take(10).ToList();
        var before = foundations.Select(f => f.Instance.Position).ToList();

        editor.Structures.Move(foundations, new Vector3(800, -1600, 400));

        var reloaded = TestEditors.Reload(editor).Structures.All;
        foreach (var p in before)
        {
            var moved = new DVector3(p.X + 800, p.Y - 1600, p.Z + 400);
            Assert.Contains(reloaded, s => s.Instance.Position == moved);
        }
    }

    [Fact]
    public void MultiTargetPlan_SkipsWhatCannotBeDeleted()
    {
        var editor = TestEditors.Open("sample2.sav");
        var actors = editor.Save.Body.AllObjects.OfType<ActorObject>().ToList();
        var storage = actors.Where(a => a.ClassName == "Build_StorageContainerMk2_C").Take(3).ToList();
        var player = actors.First(a => a.ClassName == "Char_Player_C");
        var dropPod = actors.First(a => a.ClassName == "BP_DropPod_C");
        int objectsBefore = editor.Save.Body.AllObjects.Count();

        var plan = editor.Deletion.Plan([.. storage, player, dropPod]);

        Assert.True(plan.CanDelete, plan.BlockedReason);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.Equal(storage.Count * 9, plan.ObjectsToRemove.Count); // each container + its 8 components
        editor.Deletion.Execute(plan);

        var reloaded = TestEditors.Reload(editor);
        Assert.Equal(objectsBefore - storage.Count * 9, reloaded.Save.Body.AllObjects.Count());
        Assert.NotNull(reloaded.Find(player.InstanceName));
        Assert.NotNull(reloaded.Find(dropPod.InstanceName));
    }

    [Fact]
    public void MultiTargetPlan_WithOnlyUndeletableTargets_IsBlocked()
    {
        var editor = TestEditors.Open("sample2.sav");
        var players = editor.Save.Body.AllObjects.OfType<ActorObject>().Where(a => a.ClassName == "Char_Player_C").ToList();

        var plan = editor.Deletion.Plan(players);

        Assert.False(plan.CanDelete);
        Assert.Equal(2, plan.Skipped.Count);
    }
}
