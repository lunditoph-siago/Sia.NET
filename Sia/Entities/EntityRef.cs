namespace Sia;

public readonly record struct EntityRef(long Pointer, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Pointer);
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Pointer);

    public bool Contains(Type componentType)
        => Host.Contains(Pointer, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Pointer);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Pointer);

    public readonly void Dispose()
        => Host.Release(Pointer);
}