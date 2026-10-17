using System.Text.RegularExpressions;

namespace SatisfactorySaveEditor.Domain.GameData;

/// <summary>Finds the game's Docs JSON in common Steam and Epic install locations.</summary>
public static partial class GameDataLocator
{
    private const string DocsRelativePath = @"CommunityResources\Docs";

    public static string? FindDocsJson(string culture = "en-US")
    {
        foreach (var install in CandidateInstallDirectories())
        {
            var docs = Path.Combine(install, DocsRelativePath);
            var file = Path.Combine(docs, culture + ".json");
            if (File.Exists(file))
                return file;
            var fallback = Path.Combine(docs, "en-US.json");
            if (File.Exists(fallback))
                return fallback;
        }
        return null;
    }

    private static IEnumerable<string> CandidateInstallDirectories()
    {
        foreach (var library in SteamLibraries())
            yield return Path.Combine(library, "steamapps", "common", "Satisfactory");

        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"C:\Program Files" }.Distinct())
        {
            yield return Path.Combine(programFiles, "Epic Games", "SatisfactoryExperimental");
            yield return Path.Combine(programFiles, "Epic Games", "SatisfactoryEarlyAccess");
            yield return Path.Combine(programFiles, "Epic Games", "Satisfactory");
        }
    }

    /// <summary>Steam library roots, read from libraryfolders.vdf in the default Steam install.</summary>
    private static IEnumerable<string> SteamLibraries()
    {
        var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;
        foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPathRegex();
}
