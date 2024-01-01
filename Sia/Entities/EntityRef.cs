namespace Sia;

public readonly record struct EntityRef(nint Pointer, int Version, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Pointer, Version);
    public bool Valid => Host != null && Host.IsValid(Pointer, Version);
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Pointer, Version);

    public bool Contains(Type componentType)
        => Host.Contains(Pointer, Version, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Pointer, Version);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Pointer, Version);

    public readonly void Dispose()
        => Host.Release(Pointer, Version);
}