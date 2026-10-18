using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Trailing;

namespace SatisfactorySaveEditor.Domain.Tests;

public class ConveyorTests
{
    [Fact]
    public void AllChainsAndBelts_AreDecoded()
    {
        var editor = TestEditors.Open("sample2.sav");
        var actors = editor.Save.Body.AllObjects.OfType<ActorObject>().ToList();

        Assert.All(actors.Where(a => a.ClassName.StartsWith("FGConveyorChainActor")), a => Assert.IsType<ConveyorChainTrailing>(a.Trailing));
        Assert.All(actors.Where(a => a.ClassName.StartsWith("Build_ConveyorBelt")), a => Assert.IsType<ConveyorItemsTrailing>(a.Trailing));
    }

    [Fact]
    public void ChainSegments_PointBackAtTheirChainAndBelts()
    {
        var editor = TestEditors.Open("sample2.sav");

        foreach (var chainActor in editor.Save.Body.AllObjects.OfType<ActorObject>().Where(a => a.Trailing is ConveyorChainTrailing).Take(200))
        {
            var chain = (ConveyorChainTrailing)chainActor.Trailing!;
            Assert.All(chain.Segments, s =>
            {
                Assert.Equal(chainActor.InstanceName, s.Chain.PathName);
                var belt = editor.Find(s.Belt.PathName)!;
                Assert.Equal(chainActor, editor.Conveyors.ChainOf(belt));
            });
            Assert.Equal(chain.Segments.Max(s => s.EndsAtLength), chain.TotalLength, 0.5f);
        }
    }

    [Fact]
    public void BeltContents_SumToTheChainItems()
    {
        var editor = TestEditors.Open("sample2.sav");
        var chainActor = editor.Save.Body.AllObjects.OfType<ActorObject>().First(a => a.ClassName == "FGConveyorChainActor"
            && a.InstanceName!.EndsWith("_2147473568"));
        var chain = (ConveyorChainTrailing)chainActor.Trailing!;

        int onBelts = chain.Segments.Sum(s => editor.Conveyors.ContentsOf(editor.Find(s.Belt.PathName)!).Sum(c => c.Count));

        Assert.Equal(chain.Items.Count, onBelts);
        var firstBelt = editor.Find(chain.Segments[0].Belt.PathName)!;
        var coal = Assert.Single(editor.Conveyors.ContentsOf(firstBelt));
        Assert.EndsWith("Desc_Coal_C", coal.ItemClass);
        Assert.Equal(16, coal.Count); // item indices 0..15
    }
}
