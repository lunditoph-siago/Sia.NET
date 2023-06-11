namespace Sia;

using System.Reflection;
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
    public int Size { get; }

    private Dictionary<Type, FieldInfo> _compInfos = new();
    private SparseSet<IntPtr> _compOffsets = new();

    private EntityDescriptor(Type type)
    {
        Type = type;
        Size = Marshal.SizeOf(type);

        foreach (var member in type.GetMembers()) {
            if (member.MemberType != MemberTypes.Field) {
                continue;
            }
            var fieldInfo = (FieldInfo)member;
            if (!_compInfos.TryAdd(fieldInfo.FieldType, fieldInfo)) {
                throw new ComponentTypeConflictException("Entity component types conflict");
            }
        }
    }

    public bool TryGetOffset<TComponent>(out IntPtr offset)
        => UnsafeTryGetOffset(typeof(TComponent), TypeIndexer<TComponent>.Index, out offset);

    public bool UnsafeTryGetOffset(Type componentType, int componentTypeIndex, out IntPtr offset)
    {
        if (_compOffsets.TryGetValue(componentTypeIndex, out offset)) {
            return true;
        }
        if (!_compInfos.TryGetValue(componentType, out var compInfo)) {
            offset = default;
            return false;
        }
        offset = Marshal.OffsetOf(Type, compInfo.Name);
        _compOffsets.Add(componentTypeIndex, offset);
        return true;
    }
}