namespace Maque.Majsoul;

public sealed class MajsoulProtocolException : Exception
{
    public MajsoulProtocolException(string message)
        : base(message)
    {
    }

    public MajsoulProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
