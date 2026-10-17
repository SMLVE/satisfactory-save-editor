using SatisfactorySaveEditor.Core.Model;

namespace SatisfactorySaveEditor.Core.Tests;

public class StructureTests
{
    [Fact]
    public void Sample_HasExpectedHeaderAndLayout()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample.sav"));

        Assert.Equal(14u, save.Header.HeaderVersion);
        Assert.Equal(60u, save.Header.SaveVersion);
        Assert.Equal(513885u, save.Header.BuildVersion);
        Assert.Equal("5.6.1", save.Body.VersionInfo.EngineVersion);
        Assert.Equal(7, save.Body.Grids.Count);
        Assert.Equal(248, save.Body.Levels.Count);
        Assert.True(save.Body.PersistentLevel.IsPersistent);
        Assert.Equal(1479, save.Body.AllObjects.Count());
        Assert.Empty(save.Body.Tail);
    }

    [Fact]
    public void Sample2_ParsesAllObjects()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample2.sav"));

        Assert.Equal("ClapClap", save.Header.SessionName);
        Assert.Equal(1322, save.Body.Levels.Count);
        Assert.Equal(49385, save.Body.AllObjects.Count());
        Assert.Empty(save.Body.Tail);
    }

    [Fact]
    public void ActorTransforms_AreUnitQuaternions()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample2.sav"));

        foreach (var actor in save.Body.AllObjects.OfType<ActorObject>())
        {
            float length = actor.Rotation.Length();
            Assert.True(MathF.Abs(length - 1f) < 1e-3f || length == 0f,
                $"{actor.InstanceName} has rotation length {length}");
        }
    }
}
