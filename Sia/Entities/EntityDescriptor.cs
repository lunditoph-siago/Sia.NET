namespace Sia;

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

public record struct ComponentInfo(Type Type, IntPtr Offset, int TypeIndex);

public static class EntityDescriptor<TEntity>
    where TEntity : IHList
{
    public static readonly ImmutableArray<ComponentInfo> Components;
    public static readonly FrozenDictionary<Type, IntPtr> Offsets;
    public static readonly ImmutableArray<nint> OffsetSlots;

    private unsafe struct HeadOffsetRecorder(
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
            components.Add(new(typeof(T), offset, EntityComponentIndexer<TEntity, T>.Index));
        }
    }

    private unsafe struct TailOffsetRecorder(
        List<ComponentInfo> components, Dictionary<Type, IntPtr> offsets, IntPtr entityPtr)
        : IRefGenericHandler<IHList>
    {
        public readonly void Handle<T>(ref T value) where T : IHList
        {
            value.HandleHeadRef(new HeadOffsetRecorder(components, offsets, entityPtr));
            value.HandleTailRef(new TailOffsetRecorder(components, offsets, entityPtr));
        }
    }

    unsafe static EntityDescriptor()
    {
        var components = new List<ComponentInfo>();
        var offsets = new Dictionary<Type, IntPtr>();

        var defaultEntity = default(TEntity)!;
        var entityPtr = (IntPtr)Unsafe.AsPointer(ref defaultEntity);

        defaultEntity.HandleHeadRef(new HeadOffsetRecorder(components, offsets, entityPtr));
        defaultEntity.HandleTailRef(new TailOffsetRecorder(components, offsets, entityPtr));

        Offsets = offsets.ToFrozenDictionary();
        Components = [..components];

        var slots = new nint[Components.Select(info => info.TypeIndex).Max() + 1];
        foreach (var info in Components) {
            slots[info.TypeIndex] = info.Offset;
        }
        OffsetSlots = [.. slots];
    }
}

public readonly struct EntityIndexer<TEntity, TComponent>
    where TEntity : IHList
{
    public static readonly IntPtr Offset =
        EntityDescriptor<TEntity>.Offsets.TryGetValue(typeof(TComponent), out var result)
            ? result : -1;
}

public record EntityDescriptor
{
    private interface IFieldIndexGetter
    {
        int GetIndex<TComponent>();
    }

    private class FieldIndexGetter<TEntity> : IFieldIndexGetter
    {
        public static FieldIndexGetter<TEntity> Instance = new();

        private FieldIndexGetter() {}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetIndex<TComponent>()
            => EntityComponentIndexer<TEntity, TComponent>.Index;
    }

    public static EntityDescriptor Get<TEntity>()
        where TEntity : IHList
        => new(typeof(TEntity), Unsafe.SizeOf<TEntity>(),
            EntityDescriptor<TEntity>.Components,
            EntityDescriptor<TEntity>.Offsets,
            EntityDescriptor<TEntity>.OffsetSlots,
            FieldIndexGetter<TEntity>.Instance);

    public Type Type { get; }
    public int MemorySize { get; }

    public ImmutableArray<ComponentInfo> Components { get; }
    public FrozenDictionary<Type, nint> Offsets { get; }
    public ImmutableArray<nint> OffsetSlots { get; }

    private readonly IFieldIndexGetter _fieldIndexGetter;

    private EntityDescriptor(
        Type type, int memorySize, ImmutableArray<ComponentInfo> components,
        FrozenDictionary<Type, nint> offsets, ImmutableArray<nint> offsetSlots,
        IFieldIndexGetter fieldIndexGetter)
    {
        Type = type;
        MemorySize = memorySize;
        Components = components;
        Offsets = offsets;
        OffsetSlots = offsetSlots;
        _fieldIndexGetter = fieldIndexGetter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentOffset<TComponent> GetOffset<TComponent>()
        => OffsetSlots[_fieldIndexGetter.GetIndex<TComponent>()];
}