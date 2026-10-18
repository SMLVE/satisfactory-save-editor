using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Domain;

namespace SatisfactorySaveEditor.App.Services;

/// <summary>An open save: file path, parsed save, domain editor, unsaved-changes flag and change log.</summary>
public sealed class SaveDocument
{
    private SaveDocument(string path, SaveFile save, GameDataStore gameData)
    {
        Path = path;
        Save = save;
        Editor = new SaveEditor(save, gameData.Data);
    }

    public string Path { get; private set; }
    public SaveFile Save { get; }
    public SaveEditor Editor { get; private set; }
    public bool IsDirty { get; private set; }

    /// <summary>Recreates the domain editor with newly loaded game data (the save itself is unchanged).</summary>
    public void AttachGameData(Domain.GameData.GameData? gameData) => Editor = new SaveEditor(Save, gameData);

    public string FileName => System.IO.Path.GetFileName(Path);

    public event EventHandler? DirtyChanged;

    /// <summary>Raised after objects were added to or removed from the save, so views can rebuild.</summary>
    public event EventHandler? StructureChanged;

    public void NotifyStructureChanged() => StructureChanged?.Invoke(this, EventArgs.Empty);

    public static Task<SaveDocument> LoadAsync(string path, GameDataStore gameData) =>
        Task.Run(() => new SaveDocument(path, SaveFile.Load(path), gameData));

    private readonly List<string> _changes = [];

    /// <summary>Human-readable descriptions of the edits made since the last save, oldest first.</summary>
    public IReadOnlyList<string> Changes => _changes;

    /// <summary>Records an edit for the change summary and marks the document dirty.</summary>
    public void RecordChange(string description)
    {
        _changes.Add(description);
        if (IsDirty)
            return;
        IsDirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves to <paramref name="path"/> after verifying the serialized save reads back correctly.
    /// An existing file is first copied to a timestamped .bak next to it,
    /// and the new file is written to a temporary file and moved into place so a failed save never
    /// leaves a truncated save behind. Returns the backup path, if one was made.
    /// </summary>
    public async Task<string?> SaveAsync(string path)
    {
        // Verified before anything touches the disk: a failed check throws and the existing file stays intact.
        var bytes = await Task.Run(() => Save.ToValidatedBytes());
        string? backup = null;
        if (File.Exists(path))
        {
            var dir = System.IO.Path.GetDirectoryName(path)!;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            backup = System.IO.Path.Combine(dir, $"{name}_{DateTime.Now:yyyyMMdd-HHmmss}.sav.bak");
            File.Copy(path, backup, overwrite: true);
        }
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, path, overwrite: true);

        Path = path;
        IsDirty = false;
        _changes.Clear();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return backup;
    }
}
