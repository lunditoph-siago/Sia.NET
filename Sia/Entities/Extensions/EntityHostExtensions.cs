namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    public ref struct Enumerator(IEntityHost host)
    {
        public readonly Entity Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => host.GetEntity(_slot);
        }

        private int _slotIndex = -1;
        private readonly ReadOnlySpan<StorageSlot> _slots = host.AllocatedSlots;
        private StorageSlot _slot;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_slotIndex >= 0) {
                var currSlot = _slots[_slotIndex];
                if (currSlot != _slot) {
                    _slot = currSlot;
                    return true;
                }
            }
            if (++_slotIndex >= _slots.Length) {
                _slot = _slots[_slotIndex];
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _slotIndex = -1;
        }
    }

    public static Enumerator GetEnumerator(this IEntityHost host) => new(host);
    public static Enumerator GetEnumerator(this IReactiveEntityHost host) => new(host);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Span<byte> GetSpan(this IEntityHost host, scoped in StorageSlot slot)
        => new(Unsafe.AsPointer(ref host.GetByteRef(slot)), host.Descriptor.MemorySize);
}