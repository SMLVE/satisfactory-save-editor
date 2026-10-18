using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.App.Services;
using SatisfactorySaveEditor.App.ViewModels.Explorer;
using SatisfactorySaveEditor.App.ViewModels.Players;
using SatisfactorySaveEditor.App.ViewModels.Progression;
using SatisfactorySaveEditor.Core;

namespace SatisfactorySaveEditor.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly GameDataStore _gameData = new();

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _ = LoadGameDataAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasDocument))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand))]
    private SaveDocument? _document;

    [ObservableProperty] private ExplorerViewModel? _explorer;
    [ObservableProperty] private PlayersViewModel? _players;
    [ObservableProperty] private ProgressionViewModel? _progression;

    /// <summary>Tab order in the main window: Players, Progression, Explorer.</summary>
    [ObservableProperty] private int _selectedTab;

    partial void OnSelectedTabChanged(int value)
    {
        if (Document is null)
            return;
        // Refresh the activated tab so it reflects edits made in the other tabs.
        switch (value)
        {
            case 0: Players = new PlayersViewModel(Document, Players?.SelectedPlayer?.Name); break;
            case 1: Progression?.Refresh(); break;
            case 2: Explorer?.RefreshSelection(); break;
        }
    }

    private void BuildTabs(SaveDocument doc)
    {
        Players = new PlayersViewModel(doc);
        Progression = new ProgressionViewModel(doc);
        Explorer ??= new ExplorerViewModel(doc);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(SaveCommand), nameof(SaveAsCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "Open a save to begin.";
    [ObservableProperty] private string _gameDataStatus = "Game data: loading…";

    public bool HasDocument => Document is not null;

    public string Title => Document is null
        ? "Satisfactory Save Editor"
        : $"{Document.FileName}{(Document.IsDirty ? " •" : "")} — Satisfactory Save Editor";

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var path = await _dialogs.PickSaveToOpenAsync();
        if (path is null)
            return;
        await OpenPathAsync(path);
    }

    public async Task OpenPathAsync(string path)
    {
        await RunBusyAsync($"Loading {Path.GetFileName(path)}…", async () =>
        {
            var doc = await SaveDocument.LoadAsync(path, _gameData);
            if (doc.Editor.GameData != _gameData.Data)
                doc.AttachGameData(_gameData.Data); // game data finished loading while the save was loading
            doc.DirtyChanged += (_, _) => OnPropertyChanged(nameof(Title));
            Document = doc;
            Explorer = null;
            BuildTabs(doc);
            SelectedTab = 0;
            var header = doc.Save.Header;
            int unparsed = doc.Save.Body.AllObjects.Count(o => !o.IsParsed && o.RawData.Length > 0);
            Status = $"{header.SessionName} — {header.PlayTime:d\\.hh\\:mm} played, save version {header.SaveVersion}, " +
                     $"{Explorer!.ObjectCount:N0} objects" + (unparsed > 0 ? $" ({unparsed} unparsed, read-only)" : "");
        });
    }

    private bool CanOpen() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveToAsync(Document!.Path);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsAsync()
    {
        var path = await _dialogs.PickSaveDestinationAsync(Document!.FileName);
        if (path is not null)
            await SaveToAsync(path);
    }

    private bool CanSave() => Document is not null && !IsBusy;

    private async Task SaveToAsync(string path)
    {
        await RunBusyAsync($"Saving {Path.GetFileName(path)}…", async () =>
        {
            var backup = await Document!.SaveAsync(path);
            OnPropertyChanged(nameof(Title));
            Status = backup is null ? $"Saved {path}" : $"Saved {path} (backup: {Path.GetFileName(backup)})";
        });
    }

    [RelayCommand]
    private async Task LocateGameDataAsync()
    {
        var path = await _dialogs.PickDocsJsonAsync();
        if (path is null)
            return;
        await RunBusyAsync("Loading game data…", async () =>
        {
            await _gameData.LoadAsync(path);
            UpdateGameDataStatus();
        });
    }

    /// <summary>Called by the window before closing. Returns false to keep the window open.</summary>
    public Task<bool> ConfirmCloseAsync() => ConfirmDiscardAsync();

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (Document is not { IsDirty: true })
            return true;
        var answer = await _dialogs.AskAsync("Unsaved changes",
            $"Save changes to {Document.FileName} first?", "Save", "Discard");
        if (answer is null)
            return false;
        if (answer == true)
            await SaveToAsync(Document.Path);
        return !Document.IsDirty;
    }

    private async Task LoadGameDataAsync()
    {
        try
        {
            if (!await _gameData.LoadDefaultAsync())
                GameDataStatus = "Game data: not found — use File › Locate game data";
            else
                UpdateGameDataStatus();
        }
        catch (Exception ex)
        {
            GameDataStatus = $"Game data: failed to load ({ex.Message})";
        }
    }

    private void UpdateGameDataStatus()
    {
        GameDataStatus = $"Game data: {_gameData.Data!.Items.Count} items, {_gameData.Data.Schematics.Count} schematics";
        // A save opened before game data was available gets it now.
        if (Document is not null && Document.Editor.GameData != _gameData.Data)
        {
            Document.AttachGameData(_gameData.Data);
            BuildTabs(Document);
        }
    }

    private async Task RunBusyAsync(string message, Func<Task> work)
    {
        IsBusy = true;
        var previous = Status;
        Status = message;
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is SaveFormatException or SaveValidationException or IOException
                                       or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = previous;
            await _dialogs.ShowMessageAsync("Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
