using SatisfactorySaveEditor.Core.IO;

namespace SatisfactorySaveEditor.Core.Model;

/// <summary>Reference to another object by level name and full path name.</summary>
public sealed record ObjectReference(string? LevelName, string? PathName)
{
    public static readonly ObjectReference Empty = new(null, null);

    public bool IsEmpty => string.IsNullOrEmpty(LevelName) && string.IsNullOrEmpty(PathName);

    public static ObjectReference Read(SaveReader r) => new(r.ReadFString(), r.ReadFString());

    public void Write(SaveWriter w)
    {
        w.WriteFString(LevelName);
        w.WriteFString(PathName);
    }

    public override string ToString() => IsEmpty ? "(none)" : $"{LevelName}:{PathName}";
}
