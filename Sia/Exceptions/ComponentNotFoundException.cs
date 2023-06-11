namespace Sia;

[System.Serializable]
public class ComponentNotFoundException : EntityException
{
    public ComponentNotFoundException() { }
    public ComponentNotFoundException(string message) : base(message) { }
    public ComponentNotFoundException(string message, System.Exception inner) : base(message, inner) { }
    protected ComponentNotFoundException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}