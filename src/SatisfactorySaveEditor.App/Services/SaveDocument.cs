using SatisfactorySaveEditor.Core;
using SatisfactorySaveEditor.Domain;

namespace SatisfactorySaveEditor.App.Services;

/// <summary>An open save: file path, parsed save, domain editor and unsaved-changes flag.</summary>
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
    public SaveEditor Editor { get; }
    public bool IsDirty { get; private set; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public event EventHandler? DirtyChanged;

    public static Task<SaveDocument> LoadAsync(string path, GameDataStore gameData) =>
        Task.Run(() => new SaveDocument(path, SaveFile.Load(path), gameData));

    public void MarkDirty()
    {
        if (IsDirty)
            return;
        IsDirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves to <paramref name="path"/>. An existing file is first copied to a timestamped .bak next to it,
    /// and the new file is written to a temporary file and moved into place so a failed save never
    /// leaves a truncated save behind. Returns the backup path, if one was made.
    /// </summary>
    public async Task<string?> SaveAsync(string path)
    {
        var bytes = await Task.Run(() => Save.ToBytes());
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
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return backup;
    }
}
