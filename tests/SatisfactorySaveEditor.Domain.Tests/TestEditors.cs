using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Core.Tests;

namespace SatisfactorySaveEditor.Domain.Tests;

internal static class TestEditors
{
    private static readonly Lazy<GameData.GameData> FixtureData = new(() =>
        Domain.GameData.GameData.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docs.json")));

    public static GameData.GameData GameData => FixtureData.Value;

    public static SaveEditor Open(string sample) => new(SaveFile.Load(SampleSaves.Bytes(sample)), GameData);

    /// <summary>Saves to bytes and loads again, asserting every object still parses.</summary>
    public static SaveEditor Reload(SaveEditor editor)
    {
        var reloaded = SaveFile.Load(editor.Save.ToBytes());
        Assert.All(reloaded.Body.AllObjects, o => Assert.True(o.IsParsed || o.RawData.Length == 0, o.ParseError));
        return new SaveEditor(reloaded, GameData);
    }
}
