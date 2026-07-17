namespace Sia;

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

public record struct ComponentInfo(Type Type, IntPtr Offset, int TypeIndex);

public static class EntityDescriptor<TEntity>
    where TEntity : struct, IHList
{
    public static readonly ImmutableArray<ComponentInfo> Components;
    public static readonly FrozenDictionary<Type, IntPtr> Offsets;
    public static readonly ImmutableArray<nint> OffsetSlots;

    private unsafe struct OffsetRecorder(
        List<ComponentInfo> components, Dictionary<Type, IntPtr> offsets, IntPtr entityPtr)
        : IRefGenericHandler<IHList>
    {
        private struct HeadRecorder(
            List<ComponentInfo> components, Dictionary<Type, IntPtr> offsets, IntPtr entityPtr)
            : IRefGenericHandler
        {
            public readonly void Handle<T>(ref T value)
            {
                var compPtr = (IntPtr)Unsafe.AsPointer(ref value);
                var offset = compPtr - entityPtr;
                if (!offsets.TryAdd(typeof(T), offset)) {
                    throw new InvalidDataException("Entity cannot have multiple components of the same type");
                }
                components.Add(new(typeof(T), offset, InternalComponentIndexer<T>.Index));
            }
        }

        private HeadRecorder _headRecorder = new(components, offsets, entityPtr);

        public void Handle<T>(ref T value) where T : IHList
        {
            value.HandleHeadRef(ref _headRecorder);
            value.HandleTailRef(ref this);
        }
    }

    static unsafe EntityDescriptor()
    {
        if (typeof(TEntity) == typeof(EmptyHList)) {
            Components = [];
            Offsets = new Dictionary<Type, IntPtr>().ToFrozenDictionary();
            OffsetSlots = [];
            return;
        }

        var components = new List<ComponentInfo>();
        var offsets = new Dictionary<Type, IntPtr>();

        var defaultEntity = default(TEntity)!;
        var entityPtr = (IntPtr)Unsafe.AsPointer(ref defaultEntity);

        new OffsetRecorder(components, offsets, entityPtr).Handle(ref defaultEntity);
        Offsets = offsets.ToFrozenDictionary();
        Components = [.. components];

        var slots = new nint[Components.Select(info => info.TypeIndex).Max() + 1];
        Array.Fill(slots, -1);
        foreach (var info in Components) {
            slots[info.TypeIndex] = info.Offset;
        }
        OffsetSlots = [.. slots];
    }
}

public readonly struct EntityIndexer<TEntity, TComponent>
    where TEntity : struct, IHList
{
    public static readonly IntPtr Offset =
        EntityDescriptor<TEntity>.Offsets.TryGetValue(typeof(TComponent), out var result)
            ? result : -1;
}

public record EntityDescriptor
{
    public static EntityDescriptor Get<TEntity>()
        where TEntity : struct, IHList
        => new(typeof(TEntity), Unsafe.SizeOf<TEntity>(),
            EntityDescriptor<TEntity>.Components,
            EntityDescriptor<TEntity>.Offsets,
            EntityDescriptor<TEntity>.OffsetSlots);

    public Type Type { get; }
    public int MemorySize { get; }

    public ImmutableArray<ComponentInfo> Components { get; }
    public FrozenDictionary<Type, nint> Offsets { get; }
    public ImmutableArray<nint> OffsetSlots { get; }

    private EntityDescriptor(
        Type type, int memorySize, ImmutableArray<ComponentInfo> components,
        FrozenDictionary<Type, nint> offsets, ImmutableArray<nint> offsetSlots)
    {
        Type = type;
        MemorySize = memorySize;
        Components = components;
        Offsets = offsets;
        OffsetSlots = offsetSlots;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentOffset<TComponent> GetOffset<TComponent>()
    {
        var index = InternalComponentIndexer<TComponent>.Index;
        if ((uint)index < (uint)OffsetSlots.Length) {
            var offset = OffsetSlots[index];
            if (offset >= 0) {
                return offset;
            }
        }
        return ThrowComponentNotFound<TComponent>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ComponentOffset<TComponent> GetOffsetUnchecked<TComponent>()
        => OffsetSlots[InternalComponentIndexer<TComponent>.Index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
    {
        var index = InternalComponentIndexer<TComponent>.Index;
        return (uint)index < (uint)OffsetSlots.Length
            && OffsetSlots[index] >= 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ComponentOffset<TComponent> ThrowComponentNotFound<TComponent>()
        => throw new ComponentNotFoundException(
            "Component does not exist: " + typeof(TComponent));
}
