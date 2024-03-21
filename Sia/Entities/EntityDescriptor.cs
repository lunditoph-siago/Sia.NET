namespace Sia;

using System.Collections.Frozen;
using System.Runtime.CompilerServices;

public static class EntityDescriptor<TEntity>
    where TEntity : IHList
{
    private delegate int GetOffsetDelegate(in TEntity entity);

    public static readonly FrozenDictionary<Type, IntPtr> FieldOffsets;

    private unsafe struct HeadOffsetRecorder(Dictionary<Type, IntPtr> dict, int* offset) : IGenericHandler
    {
        public readonly void Handle<T>(in T value)
        {
            if (!dict.TryAdd(typeof(T), *offset)) {
                throw new InvalidDataException("Entity cannot have multiple components of the same type");
            }
            *offset += Unsafe.SizeOf<T>();
        }
    }

    private unsafe struct TailOffsetRecorder(Dictionary<Type, IntPtr> dict, int* offset) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            var headRecorder = new HeadOffsetRecorder(dict, offset);
            value.HandleHead(headRecorder);
            value.HandleTail(new TailOffsetRecorder(dict, offset));
        }
    }

    unsafe static EntityDescriptor()
    {
        var dict = new Dictionary<Type, IntPtr>();
        var defaultEntity = default(TEntity)!;

        int offset = 0;
        var headRecorder = new HeadOffsetRecorder(dict, &offset);
        defaultEntity.HandleHead(headRecorder);
        defaultEntity.HandleTail(new TailOffsetRecorder(dict, &offset));

        FieldOffsets = dict.ToFrozenDictionary();
    }
}

public readonly struct EntityIndexer<TEntity, TComponent>
    where TEntity : IHList
{
    public static readonly IntPtr Offset =
        EntityDescriptor<TEntity>.FieldOffsets.TryGetValue(typeof(TComponent), out var result)
            ? result : -1;
}

public record EntityDescriptor
{
    private interface IProxy
    {
        public FrozenDictionary<Type, nint> FieldOffsets { get; }
        nint GetOffset<TComponent>();
    }

    private class Proxy<TEntity> : IProxy
        where TEntity : IHList
    {
        public static EntityDescriptor Descriptor = new(
            typeof(TEntity), Unsafe.SizeOf<TEntity>(), new Proxy<TEntity>());
        
        public FrozenDictionary<Type, nint> FieldOffsets => EntityDescriptor<TEntity>.FieldOffsets;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nint GetOffset<TComponent>()
            => EntityIndexer<TEntity, TComponent>.Offset;
    }

    public static EntityDescriptor Get<TEntity>()
        where TEntity : IHList
        => Proxy<TEntity>.Descriptor;

    public Type Type { get; }
    public int MemorySize { get; }
    public FrozenDictionary<Type, nint> FieldOffsets => _proxy.FieldOffsets;

    private const int OffsetCacheSize = 256;
    private readonly (Type? Type, nint Offset)[] _offsetCache = new (Type?, nint)[OffsetCacheSize];
    private readonly IProxy _proxy;

    private EntityDescriptor(Type type, int memorySize, IProxy proxy)
    {
        Type = type;
        MemorySize = memorySize;
        _proxy = proxy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint GetOffset(Type componentType)
        => FieldOffsets.TryGetValue(componentType, out var offset) ? offset : -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint GetOffset<TComponent>()
    {
        ref var entry = ref _offsetCache[TypeIndexer<TComponent>.Index % OffsetCacheSize];
        var type = typeof(TComponent);

        if (entry.Type != type) {
            entry.Type = type;
            entry.Offset = _proxy.GetOffset<TComponent>();
        }

        return entry.Offset;
    }
}