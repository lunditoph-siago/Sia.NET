[System.Serializable]
public class InvalidSystemAttributeException : System.Exception
{
    public InvalidSystemAttributeException() { }
    public InvalidSystemAttributeException(string message) : base(message) { }
    public InvalidSystemAttributeException(string message, System.Exception inner) : base(message, inner) { }
    protected InvalidSystemAttributeException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}