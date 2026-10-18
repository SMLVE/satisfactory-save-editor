using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Structures;

namespace SatisfactorySaveEditor.Domain.Tests;

public class StructureTests
{
    [Fact]
    public void LightweightBuildables_AreDecoded()
    {
        var editor = TestEditors.Open("sample2.sav");
        var subsystem = editor.Single("FGLightweightBuildableSubsystem");

        var data = Assert.IsType<LightweightBuildablesTrailing>(subsystem.Trailing);

        Assert.Equal(13, data.Classes.Count);
        Assert.Contains(data.Classes, c => c.ClassName == "Build_Foundation_8x4_01_C" && c.Instances.Count > 0);
        Assert.False(editor.Structures.IsUndecoded);
    }

    [Fact]
    public void Structures_HaveSensibleTransformsAndFootprints()
    {
        var structures = TestEditors.Open("sample2.sav").Structures.All;

        Assert.NotEmpty(structures);
        Assert.All(structures, s =>
        {
            var q = s.Instance.Rotation;
            Assert.InRange(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W, 0.999, 1.001);
            Assert.InRange(s.Position.X, -400_000f, 500_000f);
            Assert.InRange(s.Position.Y, -400_000f, 400_000f);
        });
        var foundation = structures.First(s => s.ClassName == "Build_Foundation_8x4_01_C");
        Assert.Equal(StructureKind.Foundation, foundation.Kind);
        Assert.Equal((800f, 800f), (foundation.Width, foundation.Depth));
        var wall = structures.First(s => s.Kind == StructureKind.Wall);
        Assert.True(wall.Width > wall.Depth);
    }

    [Fact]
    public void FreshSave_HasNoStructures() =>
        Assert.Empty(TestEditors.Open("sample.sav").Structures.All);

    [Fact]
    public void EditedInstance_IsWrittenFromTheDecodedModel()
    {
        var editor = TestEditors.Open("sample2.sav");
        var data = (LightweightBuildablesTrailing)editor.Single("FGLightweightBuildableSubsystem").Trailing!;
        var instance = data.Classes[0].Instances[0];
        instance.Position = instance.Position with { Z = instance.Position.Z + 100 };
        int countBefore = data.InstanceCount;

        var reloaded = (LightweightBuildablesTrailing)TestEditors.Reload(editor).Single("FGLightweightBuildableSubsystem").Trailing!;

        Assert.Equal(countBefore, reloaded.InstanceCount);
        Assert.Equal(instance.Position, reloaded.Classes[0].Instances[0].Position);
    }
}
