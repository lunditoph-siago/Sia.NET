namespace Sia;

public readonly record struct EntityRef(
    long Pointer, IEntityAccessor Accessor, IEntityDisposer? Disposer = null)
{
    public bool Contains<TComponent>()
        => Accessor.Contains<TComponent>(Pointer);

    public bool Contains(Type componentType)
        => Accessor.Contains(Pointer, componentType);

    public ref TComponent Get<TComponent>()
        => ref Accessor.Get<TComponent>(Pointer);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Accessor.GetOrNullRef<TComponent>(Pointer);

    public readonly void Destroy()
        => Disposer?.DisposeEntity(Pointer);
}