using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Trailing;

namespace SatisfactorySaveEditor.Domain.Tests;

public class PowerTests
{
    [Fact]
    public void AllPowerLines_AreDecodedAndResolved()
    {
        var editor = TestEditors.Open("sample2.sav");

        var lines = editor.Power.Lines;

        Assert.Equal(1335, lines.Count);
        Assert.All(lines, l => Assert.True(l.IsComplete, $"{l.Line.InstanceName}: {l.Description}"));
    }

    [Fact]
    public void KnownLine_ConnectsPoleToConstructor()
    {
        var editor = TestEditors.Open("sample2.sav");
        var constructor = (ActorObject)editor.Find("Persistent_Level:PersistentLevel.Build_ConstructorMk1_C_182")!;

        var line = Assert.Single(editor.Power.LinesOf(constructor));

        Assert.EndsWith("Build_PowerLine_C_1148", line.Line.InstanceName, StringComparison.Ordinal);
        Assert.Contains("Constructor Mk1", line.Description, StringComparison.Ordinal);
        var constructorEnd = line.SourceBuilding == constructor ? line.SourceConnection : line.TargetConnection;
        Assert.EndsWith(".PowerInput", constructorEnd!.InstanceName, StringComparison.Ordinal);
    }

    [Fact]
    public void EditedEndpoint_IsWrittenFromTheDecodedValue()
    {
        var editor = TestEditors.Open("sample2.sav");
        var line = (ActorObject)editor.Find("Persistent_Level:PersistentLevel.Build_PowerLine_C_1148")!;
        var ends = (PowerLineTrailing)line.Trailing!;
        var original = ends.Target;

        ends.Target = original with { PathName = original.PathName + "X" };
        Assert.EndsWith("X\0", System.Text.Encoding.Latin1.GetString(line.TrailingData), StringComparison.Ordinal);

        ends.Target = original;
        var reloaded = TestEditors.Reload(editor);
        var reloadedEnds = (PowerLineTrailing)reloaded.Find(line.InstanceName)!.Trailing!;
        Assert.Equal(original, reloadedEnds.Target);
    }
}
