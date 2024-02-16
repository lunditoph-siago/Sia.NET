namespace Sia;

using System.Runtime.CompilerServices;

public static class EntityQueryExtensions
{
    public ref struct Enumerator(IReadOnlyList<IEntityHost> hosts)
    {
        private int _hostIndex;
        private int _slotIndex = -1;

        private IEntityHost _host;
        private ReadOnlySpan<StorageSlot> _slots;

        public readonly EntityRef Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(_slots[_slotIndex], _host);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true) {
                if (_hostIndex >= hosts.Count) {
                    return false;
                }
                if (++_slotIndex == 0) {
                    _host = hosts[_hostIndex];
                    _slots = _host.AllocatedSlots;
                }
                if (_slotIndex < _slots.Length) {
                    return true;
                }
                _hostIndex++;
                _slotIndex = -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _hostIndex = 0;
            _slotIndex = -1;
        }
    }

    public static Enumerator GetEnumerator(this IEntityQuery query)
        => new(query.Hosts);
}