namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    public ref struct Enumerator(IEntityHost host)
    {
        public readonly Entity Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities[_slot];
        }

        private int _slot = -1;
        private readonly Span<Entity> _entities = host.UnsafeGetEntitySpan();
        private readonly int _version = host.Version;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (host.Version != _version) {
                throw new InvalidOperationException("Entity host was modified; enumeration operation may not execute");
            }
            return ++_slot < _entities.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _slot = -1;
    }

    public static Enumerator GetEnumerator(this IEntityHost host) => new(host);
    public static Enumerator GetEnumerator(this IReactiveEntityHost host) => new(host);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Span<byte> GetBytes(this IEntityHost host, int slot)
        => new(Unsafe.AsPointer(ref host.GetByteRef(slot)), host.Descriptor.MemorySize);
}