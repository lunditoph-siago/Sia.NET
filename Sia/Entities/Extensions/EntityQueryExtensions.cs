namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityQueryExtensions
{
    public ref struct Enumerator(IEntityQuery query)
    {
        public readonly Entity Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities[_slot];
        }

        private int _slot = -1;
        private Span<Entity> _entities;

        private readonly int _queryVersion = query.Version;
        private readonly IReadOnlyList<IEntityHost> _hosts = query.Hosts;
        private readonly int _hostCount = query.Hosts.Count;

        private IEntityHost _host;
        private int _hostIndex;
        private int _hostVersion;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_hostIndex >= _hostCount) {
                return false;
            }
            if (_queryVersion != query.Version) {
                throw new InvalidOperationException("Entity query was modified; enumeration operation may not execute");
            }

            if (_host == null) {
                _host = _hosts[_hostIndex];
                _hostVersion = _host.Version;
                _entities = _host.UnsafeGetEntitySpan();
            }
            else if (_hostVersion != _host.Version) {
                throw new InvalidOperationException("Entity host was modified; enumeration operation may not execute");
            }

            while (true) {
                if (++_slot < _entities.Length) {
                    return true;
                }
                if (++_hostIndex >= _hostCount) {
                    return false;
                }
                _slot = -1;
                _host = _hosts[_hostIndex];
                _hostVersion = _host.Version;
                _entities = _host.UnsafeGetEntitySpan();
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
        => new(query);
}