namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    public ref struct Enumerator(IEntityHost host)
    {
        public readonly Entity Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entity;
        }

        private int _slot = -1;
        private Entity _entity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int count = host.Count;
            if (_slot >= count) {
                return false;
            }
            if (_slot >= 0) {
                var currEntity = host.GetEntity(_slot);
                if (currEntity != _entity) {
                    _entity = currEntity;
                    return true;
                }
            }
            if (++_slot < host.Count) {
                _entity = host.GetEntity(_slot);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _slot = -1;
    }

    public static Enumerator GetEnumerator(this IEntityHost host) => new(host);
    public static Enumerator GetEnumerator(this IReactiveEntityHost host) => new(host);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Span<byte> GetSpan(this IEntityHost host, int slot)
        => new(Unsafe.AsPointer(ref host.GetByteRef(slot)), host.Descriptor.MemorySize);
}