namespace Sia;

[System.Serializable]
public class EntityException : System.Exception
{
    public EntityException() { }
    public EntityException(string message) : base(message) { }
    public EntityException(string message, System.Exception inner) : base(message, inner) { }
    protected EntityException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}