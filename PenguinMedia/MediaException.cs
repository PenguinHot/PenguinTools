namespace PenguinMedia;

public class MediaException : Exception
{
    public MediaException()
    {
    }

    public MediaException(string message) : base(message)
    {
    }

    public MediaException(string message, Exception inner) : base(message, inner)
    {
    }
}