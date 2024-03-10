namespace Sia;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

public static class EntityDescriptor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>
{
    private delegate int GetOffsetDelegate(in TEntity entity);

    public static readonly FrozenDictionary<Type, IntPtr> FieldOffsets;

    private const BindingFlags s_bindingFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static EntityDescriptor()
    {
        var dict = new Dictionary<Type, IntPtr>();
        RegisterFields(dict, typeof(TEntity));
        FieldOffsets = dict.ToFrozenDictionary();
    }

    private static void RegisterFields(Dictionary<Type, IntPtr> dict, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, int baseOffset = 0)
    {
        foreach (var member in type.GetMembers(s_bindingFlags)) {
            if (member.MemberType != MemberTypes.Field) {
                continue;
            }

            var fieldInfo = (FieldInfo)member;
            var offset = GetFieldOffset(fieldInfo);

            var fieldType = fieldInfo.FieldType;
            if (fieldType.IsAssignableTo(typeof(IComponentBundle))
                    || fieldInfo.GetCustomAttribute<ComponentBundleAttribute>() != null) {
                RegisterFields(dict, fieldType, baseOffset + offset);
            }
            else if (!dict.TryAdd(fieldType, baseOffset + offset)) {
                throw new InvalidDataException("Entity cannot have multiple components of the same type");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int GetFieldOffset(FieldInfo fieldInfo)
    {
        var ptr = fieldInfo.FieldHandle.Value;
        ptr = ptr + 4 + sizeof(IntPtr);
        ushort length = *(ushort*)ptr;
        byte chunkSize = *(byte*)(ptr + 2);
        return length + (chunkSize << 16);
    }
}

public readonly struct EntityIndexer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TComponent>
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

    private class Proxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity> : IProxy
    {
        public static EntityDescriptor Descriptor = new(
            typeof(TEntity), Unsafe.SizeOf<TEntity>(), new Proxy<TEntity>());
        
        public FrozenDictionary<Type, nint> FieldOffsets => EntityDescriptor<TEntity>.FieldOffsets;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nint GetOffset<TComponent>()
            => EntityIndexer<TEntity, TComponent>.Offset;
    }

    public static EntityDescriptor Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>()
        => Proxy<TEntity>.Descriptor;

    public Type Type { get; }
    public int MemorySize { get; }
    public FrozenDictionary<Type, nint> FieldOffsets { get; }

    private const int OffsetCacheSize = 256;
    private readonly (Type? Type, nint Offset)[] _offsetCache = new (Type?, nint)[OffsetCacheSize];
    private readonly IProxy _proxy;

    private EntityDescriptor(Type type, int memorySize, IProxy proxy)
    {
        Type = type;
        MemorySize = memorySize;
        _proxy = proxy;
        FieldOffsets = _proxy.FieldOffsets;
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