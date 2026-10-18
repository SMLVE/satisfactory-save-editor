using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace SatisfactorySaveEditor.App.Services;

internal interface IDialogService
{
    Task<string?> PickSaveToOpenAsync();
    Task<string?> PickSaveDestinationAsync(string suggestedName);
    Task<string?> PickDocsJsonAsync();
    Task ShowMessageAsync(string title, string message);
    /// <summary>Asks a yes/no/cancel question. Returns true, false, or null for cancel.</summary>
    Task<bool?> AskAsync(string title, string message, string yes, string no);
    /// <summary>Shows the list of pending changes and asks whether to write them. Returns true to save.</summary>
    Task<bool> ConfirmSaveAsync(string fileName, IReadOnlyList<string> changes);
    /// <summary>Shows a message with a list of details and asks for confirmation. Returns true to proceed.</summary>
    Task<bool> ConfirmListAsync(string title, string message, IReadOnlyList<string> details, string confirmLabel);
}

internal sealed class DialogService(Window owner) : IDialogService
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
        return files is [var first, ..] ? first.TryGetLocalPath() : null;
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
        return files is [var first, ..] ? first.TryGetLocalPath() : null;
    }

    public async Task ShowMessageAsync(string title, string message) =>
        await ShowDialog(title, message, [("OK", (bool?)true)]);

    public Task<bool?> AskAsync(string title, string message, string yes, string no) =>
        ShowDialog(title, message, [(yes, true), (no, false), ("Cancel", null)]);

    public Task<bool> ConfirmSaveAsync(string fileName, IReadOnlyList<string> changes)
    {
        var noun = changes.Count == 1 ? "change" : "changes";
        return ConfirmListAsync("Save changes", $"Write {changes.Count} {noun} to {fileName}? A backup of the current file is made first.",
            changes.Select((c, i) => $"{i + 1}. {c}").ToList(), "Save");
    }

    public async Task<bool> ConfirmListAsync(string title, string message, IReadOnlyList<string> details, string confirmLabel)
    {
        var list = new ListBox { ItemsSource = details, MaxHeight = 360, FontSize = 12 };
        return await ShowDialog(title, message, [(confirmLabel, true), ("Cancel", null)], list, width: 640) == true;
    }

    private async Task<IStorageFolder?> StartFolder(string path) =>
        Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path) : null;

    private async Task<bool?> ShowDialog(string title, string message, (string Label, bool? Result)[] buttons,
        Control? detail = null, double width = 440)
    {
        bool? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = width,
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
        var content = new StackPanel { Margin = new(20), Spacing = 16 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (detail is not null)
            content.Children.Add(detail);
        content.Children.Add(buttonRow);
        dialog.Content = content;
        await dialog.ShowDialog(owner);
        return result;
    }
}
