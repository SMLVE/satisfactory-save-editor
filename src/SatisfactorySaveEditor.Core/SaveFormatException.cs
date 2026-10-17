namespace SatisfactorySaveEditor.Core;

public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
