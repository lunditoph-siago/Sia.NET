namespace Sia;

[Serializable]
public class ComponentTypeConflictException : EntityException
{
    public ComponentTypeConflictException() { }
    public ComponentTypeConflictException(string message) : base(message) { }
    public ComponentTypeConflictException(string message, Exception inner) : base(message, inner) { }
    protected ComponentTypeConflictException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}