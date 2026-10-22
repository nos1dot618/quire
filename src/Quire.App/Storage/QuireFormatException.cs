namespace Quire.Storage;

/// <summary>Thrown when a file is not a valid or supported Quire document. The message is shown to the user.</summary>
public sealed class QuireFormatException : Exception
{
    public QuireFormatException(string message) : base(message) { }

    public QuireFormatException(string message, Exception innerException) : base(message, innerException) { }
}
