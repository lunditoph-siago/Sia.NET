namespace Sia;

using System.Reflection;
using System.Runtime.CompilerServices;

public record EntityDescriptor
{
    private static class Indexer<TEntity>
    {
        public static EntityDescriptor Descriptor = new(typeof(TEntity));
    }

    public static EntityDescriptor Get<TEntity>()
        => Indexer<TEntity>.Descriptor;

    public Type Type { get; }

    private readonly Dictionary<Type, IntPtr> _rawCompOffsets = new();
    private readonly ThreadLocal<Dictionary<int, IntPtr>> _compOffsets = new(() => new());

    private const BindingFlags s_bindingFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private EntityDescriptor(Type type)
    {
        Type = type;
        RegisterFields(type);
    }

    private void RegisterFields(Type type, int baseOffset = 0)
    {
        foreach (var member in type.GetMembers(s_bindingFlags)) {
            if (member.MemberType != MemberTypes.Field) {
                continue;
            }

            var fieldInfo = (FieldInfo)member;
            var offset = GetFieldOffset(fieldInfo);

            var fieldType = fieldInfo.FieldType;
            if (fieldType.IsAssignableTo(typeof(IComponentBundle))) {
                RegisterFields(fieldType, baseOffset + offset);
            }
            else if (!_rawCompOffsets.TryAdd(fieldType, baseOffset + offset)) {
                throw new InvalidDataException("Entity cannot have multiple components of the same type");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type type)
        => _rawCompOffsets.ContainsKey(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => _rawCompOffsets.ContainsKey(typeof(TComponent));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetOffset<TComponent>(out IntPtr offset)
        => UnsafeTryGetOffset(typeof(TComponent), TypeIndexer<TComponent>.Index, out offset);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool UnsafeTryGetOffset(Type type, int componentTypeIndex, out IntPtr offset)
    {
        var compOffsets = _compOffsets.Value!;
        if (compOffsets.TryGetValue(componentTypeIndex, out offset)) {
            return true;
        }
        if (!_rawCompOffsets.TryGetValue(type, out offset)) {
            return false;
        }
        compOffsets.Add(componentTypeIndex, offset);
        return true;
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