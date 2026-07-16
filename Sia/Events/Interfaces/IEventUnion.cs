namespace Sia;

public interface IEventUnion
{
    ITypeUnion EventTypes { get; }
    void Handle(IGenericTypeHandler<IEvent> handler);
}

public interface IEventUnion<TSelf> : IEventUnion
    where TSelf : IEventUnion<TSelf>
{
    static abstract void HandleEventTypes(IGenericTypeHandler<IEvent> handler);

    void IEventUnion.Handle(IGenericTypeHandler<IEvent> handler)
        => TSelf.HandleEventTypes(handler);
}
