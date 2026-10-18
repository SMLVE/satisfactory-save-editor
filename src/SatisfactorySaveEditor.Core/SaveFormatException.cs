namespace SatisfactorySaveEditor.Core;

public sealed class SaveFormatException : Exception
{
    public SaveFormatException() { }

    public SaveFormatException(string message) : base(message) { }

    public SaveFormatException(string message, Exception? inner) : base(message, inner) { }
}
