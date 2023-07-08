#pragma warning disable CS8500

namespace Sia;

using System.Runtime.CompilerServices;

public class UnmanagedEntityAccessor<T> : IEntityAccessor
{
    public static UnmanagedEntityAccessor<T> Instance { get; } = new();

    public static EntityDescriptor Descriptor { get; }
        = EntityDescriptor.Get<T>();

    private UnmanagedEntityAccessor() {}

    public bool Contains<TComponent>(long pointer)
        => Descriptor.Contains<TComponent>();

    public bool Contains(long pointer, Type type)
        => Descriptor.Contains(type);

    public unsafe ref TComponent Get<TComponent>(long pointer)
    {
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref *(TComponent*)(pointer + offset);
    }

    public unsafe ref TComponent GetOrNullRef<TComponent>(long pointer)
    {
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref *(TComponent*)(pointer + offset);
    }
}