namespace Sia;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public record EntityDescriptor
{
    private static class Indexer<TEntity>
    {
        public static EntityDescriptor Descriptor = new(typeof(TEntity));
    }

    public static EntityDescriptor Get<TEntity>()
        => Indexer<TEntity>.Descriptor;

    public Type Type { get; }

    private readonly Dictionary<Type, FieldInfo> _compInfos = new();
    private readonly SparseSet<IntPtr> _compOffsets = new(512, 512);

    private const BindingFlags s_bindingFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private EntityDescriptor(Type type)
    {
        Type = type;

        foreach (var member in type.GetMembers(s_bindingFlags)) {
            if (member.MemberType != MemberTypes.Field) {
                continue;
            }
            var fieldInfo = (FieldInfo)member;
            if (!_compInfos.TryAdd(fieldInfo.FieldType, fieldInfo)) {
                throw new ComponentTypeConflictException("Entity component types conflict");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type type)
        => _compInfos.ContainsKey(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => _compInfos.ContainsKey(typeof(TComponent));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetOffset<TComponent>(out IntPtr offset)
        => UnsafeTryGetOffset(typeof(TComponent), TypeIndexer<TComponent>.Index, out offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static int GetFieldOffset(FieldInfo fieldInfo)
    {
        var ptr = fieldInfo.FieldHandle.Value;
        ptr = ptr + 4 + sizeof(IntPtr);
        ushort length = *(ushort*)ptr;
        byte chunkSize = *(byte*)(ptr + 2);
        return length + (chunkSize << 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool UnsafeTryGetOffset(Type componentType, int componentTypeIndex, out IntPtr offset)
    {
        if (_compOffsets.TryGetValue(componentTypeIndex, out offset)) {
            return true;
        }
        if (!_compInfos.TryGetValue(componentType, out var compInfo)) {
            offset = default;
            return false;
        }
        offset = GetFieldOffset(compInfo);
        _compOffsets.Add(componentTypeIndex, offset);
        return true;
    }
}