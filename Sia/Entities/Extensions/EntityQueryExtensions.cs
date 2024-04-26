namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityQueryExtensions
{
    public ref struct Enumerator(IReadOnlyList<IEntityHost> hosts)
    {
        public readonly EntityRef Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(_slot, _host);
        }

        private int _hostIndex;
        private int _slotIndex = -1;

        private IEntityHost _host;
        private ReadOnlySpan<StorageSlot> _slots;
        private StorageSlot _slot;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_hostIndex >= hosts.Count) {
                return false;
            }
            
            var currHost = hosts[_hostIndex];
            if (currHost != _host) {
                _host = currHost;
                _slots = _host.AllocatedSlots;
                _slotIndex = -1;
            }

            while (true) {
                if (_slotIndex >= 0) {
                    var currSlot = _slots[_slotIndex];
                    if (currSlot != _slot) {
                        _slot = currSlot;
                        return true;
                    }
                }
                if (++_slotIndex < _slots.Length) {
                    _slot = _slots[_slotIndex];
                    return true;
                }
                if (++_hostIndex >= hosts.Count) {
                    return false;
                }
                _host = hosts[_hostIndex];
                _slots = _host.AllocatedSlots;
                _slotIndex = -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _host = null!;
            _hostIndex = 0;
            _slotIndex = -1;
        }
    }

    public static Enumerator GetEnumerator(this IEntityQuery query)
        => new(query.Hosts);
}