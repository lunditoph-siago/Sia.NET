namespace Sia;

[System.Serializable]
public class ComponentTypeConflictException : EntityException
{
    public ComponentTypeConflictException() { }
    public ComponentTypeConflictException(string message) : base(message) { }
    public ComponentTypeConflictException(string message, System.Exception inner) : base(message, inner) { }
    protected ComponentTypeConflictException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}