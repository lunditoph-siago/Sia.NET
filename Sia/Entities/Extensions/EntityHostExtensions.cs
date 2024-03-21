namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
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
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Span<byte> GetSpan(this IEntityHost host, scoped in StorageSlot slot)
        => new(Unsafe.AsPointer(ref host.GetByteRef(slot)), host.Descriptor.MemorySize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Identity GetIdentity(this IEntityHost host, in StorageSlot slot)
        => Unsafe.As<byte, Identity>(ref host.GetByteRef(slot));
}