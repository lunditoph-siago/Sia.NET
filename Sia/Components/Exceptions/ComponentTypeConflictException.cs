namespace Sia;

[Serializable]
public class ComponentTypeConflictException : EntityException
{
    public ComponentTypeConflictException() { }
    public ComponentTypeConflictException(string message) : base(message) { }
    public ComponentTypeConflictException(string message, Exception inner) : base(message, inner) { }
}