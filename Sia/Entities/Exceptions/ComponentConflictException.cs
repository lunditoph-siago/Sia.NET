namespace Sia;

[Serializable]
public class ComponentConflictException : EntityException
{
    public ComponentConflictException() { }
    public ComponentConflictException(string message) : base(message) { }
    public ComponentConflictException(string message, Exception inner) : base(message, inner) { }
}