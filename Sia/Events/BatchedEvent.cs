namespace Sia;

public class BatchedEvent<TEvent, TTarget> : PooledEvent<BatchedEvent<TEvent, TTarget>>
    where TEvent : IEvent
    where TTarget : notnull
{
    public List<(TTarget, TEvent)> Events { get; } = new();

    public override void Dispose()
    {
        Events.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}