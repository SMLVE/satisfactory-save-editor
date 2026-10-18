namespace SatisfactorySaveEditor.Domain.GameData;

/// <summary>Thrown when a file is not the game's Docs JSON (invalid JSON or an unexpected layout).</summary>
public sealed class GameDataException : Exception
{
    public GameDataException() { }

    public GameDataException(string message) : base(message) { }

    public GameDataException(string message, Exception? inner) : base(message, inner) { }
}
