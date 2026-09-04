namespace Sia.Reactive;

public sealed class ContextScope(Type contextType, Entity providerSlot, ContextScope? parent)
{
    private readonly Dictionary<long, CellSlot> _consumers = [];

    public readonly Type ContextType = contextType;
    public readonly Entity ProviderSlot = providerSlot;
    public readonly ContextScope? Parent = parent;

    internal void AddConsumer(long identity, Entity cell)
        => _consumers[identity] = new(cell);

    internal void RemoveConsumer(long identity)
        => _consumers.Remove(identity);

    internal void InvalidateConsumers(Reconciler reconciler)
    {
        List<long>? stale = null;
        foreach (var (identity, consumer) in _consumers) {
            if (reconciler.Validate(consumer) is { IsValid: true } cell) {
                reconciler.EnqueueDirty(cell);
            }
            else {
                (stale ??= []).Add(identity);
            }
        }
        if (stale != null) {
            foreach (var identity in stale) {
                _consumers.Remove(identity);
            }
        }
    }
}
