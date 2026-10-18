using System.Text.Json;
using SatisfactorySaveEditor.Domain.GameData;

namespace SatisfactorySaveEditor.App.Services;

/// <summary>Loads game data from the game install and remembers a manually chosen Docs JSON path.</summary>
internal sealed class GameDataStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SatisfactorySaveEditor", "settings.json");

    public GameData? Data { get; private set; }
    public string? DocsPath { get; private set; }

    /// <summary>Loads from the remembered path, else from an auto-detected install. Returns false if none was found.</summary>
    public async Task<bool> LoadDefaultAsync()
    {
        var path = ReadSettings()?.DocsPath;
        if (path is null || !File.Exists(path))
            path = GameDataLocator.FindDocsJson();
        return path is not null && await LoadAsync(path, remember: false);
    }

    public async Task<bool> LoadAsync(string docsPath, bool remember = true)
    {
        Data = await Task.Run(() => GameData.Load(docsPath));
        DocsPath = docsPath;
        if (remember)
            WriteSettings(new Settings(docsPath));
        return true;
    }

    private sealed record Settings(string? DocsPath);

    private static Settings? ReadSettings()
    {
        try
        {
            return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteSettings(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}
