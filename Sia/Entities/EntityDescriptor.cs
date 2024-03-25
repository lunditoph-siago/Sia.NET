namespace Sia;

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class EntityDescriptor<TEntity>
    where TEntity : IHList
{
    public static readonly FrozenDictionary<Type, IntPtr> FieldOffsets;
    public static readonly ImmutableArray<IntPtr> FieldOffsetsArray;

    private unsafe struct HeadOffsetRecorder(
        Dictionary<Type, IntPtr> dict, List<IntPtr> list, IntPtr entityPtr)
        : IRefGenericHandler
    {
        public readonly void Handle<T>(ref T value)
        {
            var compPtr = (IntPtr)Unsafe.AsPointer(ref value);
            var offset = compPtr - entityPtr;
            if (!dict.TryAdd(typeof(T), offset)) {
                throw new InvalidDataException("Entity cannot have multiple components of the same type");
            }
            var index = EntityComponentIndexer<TEntity, T>.Index;
            if (index >= list.Count) {
                CollectionsMarshal.SetCount(list, index + 1);
            }
            list[index] = offset;
        }
    }

    private unsafe struct TailOffsetRecorder(
        Dictionary<Type, IntPtr> dict, List<IntPtr> list, IntPtr entityPtr)
        : IRefGenericHandler<IHList>
    {
        public readonly void Handle<T>(ref T value) where T : IHList
        {
            value.HandleHeadRef(new HeadOffsetRecorder(dict, list, entityPtr));
            value.HandleTailRef(new TailOffsetRecorder(dict, list, entityPtr));
        }
    }

    unsafe static EntityDescriptor()
    {
        var dict = new Dictionary<Type, IntPtr>();
        var list = new List<IntPtr>();

        var defaultEntity = default(TEntity)!;
        var entityPtr = (IntPtr)Unsafe.AsPointer(ref defaultEntity);

        defaultEntity.HandleHeadRef(new HeadOffsetRecorder(dict, list, entityPtr));
        defaultEntity.HandleTailRef(new TailOffsetRecorder(dict, list, entityPtr));

        FieldOffsets = dict.ToFrozenDictionary();
        FieldOffsetsArray = [..list];
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
            EntityDescriptor<TEntity>.FieldOffsets, EntityDescriptor<TEntity>.FieldOffsetsArray,
            FieldIndexGetter<TEntity>.Instance);

    public Type Type { get; }
    public int MemorySize { get; }
    public FrozenDictionary<Type, nint> FieldOffsets { get; }

    private readonly ImmutableArray<nint> _fieldOffsetsArr;
    private readonly IFieldIndexGetter _fieldIndexGetter;

    private EntityDescriptor(
        Type type, int memorySize, FrozenDictionary<Type, nint> fieldOffsets, ImmutableArray<nint> fieldOffsetsArr,
        IFieldIndexGetter fieldIndexGetter)
    {
        Type = type;
        MemorySize = memorySize;
        FieldOffsets = fieldOffsets;
        _fieldOffsetsArr = fieldOffsetsArr;
        _fieldIndexGetter = fieldIndexGetter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint GetOffset<TComponent>()
        => _fieldOffsetsArr[_fieldIndexGetter.GetIndex<TComponent>()];
}