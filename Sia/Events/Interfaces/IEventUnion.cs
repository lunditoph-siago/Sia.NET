namespace Sia;

public interface IEventUnion
{
    ITypeUnion EventTypes { get; }
    static abstract void HandleEventTypes(IGenericTypeHandler<IEvent> handler);
}