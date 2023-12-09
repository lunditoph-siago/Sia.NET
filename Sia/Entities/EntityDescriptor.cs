namespace Sia;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class EntityDescriptor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>
{
    private delegate int GetOffsetDelegate(in TEntity entity);

    public static FrozenDictionary<Type, IntPtr> FieldOffsets;

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
            if (fieldType.IsAssignableTo(typeof(IComponentBundle))) {
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

internal static class EntityIndexer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TComponent>
{
    public static IntPtr? Offset { get; }

    static EntityIndexer()
    {
        if (EntityDescriptor<TEntity>.FieldOffsets.TryGetValue(typeof(TComponent), out var result)) {
            Offset = result;
        }
    }
}

public record EntityDescriptor
{
    private interface IProxy
    {
        bool Contains(Type type);
        bool Contains<TComponent>();
        bool TryGetOffset<TComponent>(out IntPtr offset);
    }

    private class Proxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity> : IProxy
    {
        public static EntityDescriptor Descriptor = new(typeof(TEntity), new Proxy<TEntity>());

        public bool Contains(Type type)
            => EntityDescriptor<TEntity>.FieldOffsets.ContainsKey(type);

        public bool Contains<TComponent>()
            => EntityIndexer<TEntity, TComponent>.Offset.HasValue;

        public bool TryGetOffset<TComponent>(out nint offset)
        {
            if (EntityIndexer<TEntity, TComponent>.Offset is IntPtr raw) {
                offset = raw;
                return true;
            }
            offset = default;
            return false;
        }
    }

    public static EntityDescriptor Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>()
        => Proxy<TEntity>.Descriptor;

    public Type Type { get; }

    private readonly IProxy _proxy;

    private EntityDescriptor(Type type, IProxy proxy)
    {
        Type = type;
        _proxy = proxy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type type)
        => _proxy.Contains(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => _proxy.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetOffset<TComponent>(out IntPtr offset)
        => _proxy.TryGetOffset<TComponent>(out offset);
}