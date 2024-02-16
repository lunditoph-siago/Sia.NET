namespace Sia;

using System.Runtime.CompilerServices;

public static class EntityHostExtensions
{
    public ref struct Enumerator(IEntityHost host)
    {
        public readonly EntityRef Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(_slots[_slotIndex], host);
        }

        private int _slotIndex = -1;
        private readonly ReadOnlySpan<StorageSlot> _slots = host.AllocatedSlots;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_slotIndex < _slots.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _slotIndex = -1;
        }
    }

    public static Enumerator GetEnumerator(this IEntityHost host) => new(host);
    public static Enumerator GetEnumerator(this IReactiveEntityHost host) => new(host);
}