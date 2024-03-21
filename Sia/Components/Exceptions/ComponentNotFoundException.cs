namespace Sia;

[Serializable]
public class ComponentNotFoundException : EntityException
{
    public ComponentNotFoundException() {}
    public ComponentNotFoundException(string message) : base(message) {}
    public ComponentNotFoundException(string message, Exception inner) : base(message, inner) {}
}