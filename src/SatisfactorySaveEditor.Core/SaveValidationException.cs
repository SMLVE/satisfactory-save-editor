namespace SatisfactorySaveEditor.Core;

/// <summary>Thrown when an edited save fails verification and must not be written.</summary>
public sealed class SaveValidationException : Exception
{
    public SaveValidationException() { }

    public SaveValidationException(string message) : base(message) { }

    public SaveValidationException(string message, Exception? inner) : base(message, inner) { }
}
