using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace SatisfactorySaveEditor.App.Services;

public interface IDialogService
{
    Task<string?> PickSaveToOpenAsync();
    Task<string?> PickSaveDestinationAsync(string suggestedName);
    Task<string?> PickDocsJsonAsync();
    Task ShowMessageAsync(string title, string message);
    /// <summary>Asks a yes/no/cancel question. Returns true, false, or null for cancel.</summary>
    Task<bool?> AskAsync(string title, string message, string yes, string no);
}

public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType SaveFiles = new("Satisfactory saves") { Patterns = ["*.sav"] };
    private static readonly FilePickerFileType JsonFiles = new("Game Docs JSON") { Patterns = ["*.json"] };

    /// <summary>%LOCALAPPDATA%\FactoryGame\Saved\SaveGames, where the game keeps saves per account.</summary>
    private static string SaveGamesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FactoryGame", "Saved", "SaveGames");

    public async Task<string?> PickSaveToOpenAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open save",
            FileTypeFilter = [SaveFiles],
            SuggestedStartLocation = await StartFolder(SaveGamesFolder),
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickSaveDestinationAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as",
            SuggestedFileName = suggestedName,
            DefaultExtension = "sav",
            FileTypeChoices = [SaveFiles],
            SuggestedStartLocation = await StartFolder(SaveGamesFolder),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickDocsJsonAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Locate the game's CommunityResources/Docs/en-US.json",
            FileTypeFilter = [JsonFiles],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task ShowMessageAsync(string title, string message) =>
        await ShowDialog(title, message, [("OK", (bool?)true)]);

    public Task<bool?> AskAsync(string title, string message, string yes, string no) =>
        ShowDialog(title, message, [(yes, true), (no, false), ("Cancel", null)]);

    private async Task<IStorageFolder?> StartFolder(string path) =>
        Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path) : null;

    private async Task<bool?> ShowDialog(string title, string message, (string Label, bool? Result)[] buttons)
    {
        bool? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, value) in buttons)
        {
            var button = new Button { Content = label, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttonRow,
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }
}
