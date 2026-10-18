namespace SatisfactorySaveEditor.Core;

/// <summary>Thrown when an edited save fails verification and must not be written.</summary>
public sealed class SaveValidationException(string message, Exception? inner = null) : Exception(message, inner);
