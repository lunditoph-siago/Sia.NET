namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityQueryExtensions
{
    public ref struct Enumerator(IReadOnlyList<IEntityHost> hosts)
    {
        public readonly Entity Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entity;
        }

        private int _hostIndex;
        private int _slot = -1;
        private Entity _entity;

        private IEntityHost _host;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_hostIndex >= hosts.Count) {
                return false;
            }
            
            var currHost = hosts[_hostIndex];
            if (currHost != _host) {
                _host = currHost;
                _slot = -1;
            }

            while (true) {
                int count = _host.Count;
                if (_slot >= count) {
                    return false;
                }
                if (_slot >= 0) {
                    var currEntity = _host.GetEntity(_slot);
                    if (currEntity != _entity) {
                        _entity = currEntity;
                        return true;
                    }
                }
                if (++_slot < count) {
                    _entity = _host.GetEntity(_slot);
                    return true;
                }
                if (++_hostIndex >= hosts.Count) {
                    return false;
                }
                _host = hosts[_hostIndex];
                _slot = -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _host = null!;
            _hostIndex = 0;
            _slot = -1;
        }
    }

    public static Enumerator GetEnumerator(this IEntityQuery query)
        => new(query.Hosts);
}