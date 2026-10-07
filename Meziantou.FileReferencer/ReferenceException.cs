namespace Meziantou.FileReferencer;

/// <summary>An error that prevents a reference from being updated. The original content of the reference is preserved.</summary>
internal sealed class ReferenceException : Exception
{
    public ReferenceException()
    {
    }

    public ReferenceException(string message)
        : base(message)
    {
    }

    public ReferenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
