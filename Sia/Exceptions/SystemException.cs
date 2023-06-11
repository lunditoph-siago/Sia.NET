namespace Sia;

[System.Serializable]
public class SystemException : System.Exception
{
    public SystemException() { }
    public SystemException(string message) : base(message) { }
    public SystemException(string message, System.Exception inner) : base(message, inner) { }
    protected SystemException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}