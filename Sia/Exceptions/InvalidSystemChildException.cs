namespace Sia;

[Serializable]
public class InvalidSystemChildException : Exception
{
    public InvalidSystemChildException() { }
    public InvalidSystemChildException(string message) : base(message) { }
    public InvalidSystemChildException(string message, Exception inner) : base(message, inner) { }
    protected InvalidSystemChildException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}