namespace Sia;

[System.Serializable]
public class SystemAlreadyRegisteredException : System.Exception
{
    public SystemAlreadyRegisteredException() { }
    public SystemAlreadyRegisteredException(string message) : base(message) { }
    public SystemAlreadyRegisteredException(string message, System.Exception inner) : base(message, inner) { }
    protected SystemAlreadyRegisteredException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}