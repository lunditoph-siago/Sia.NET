using System.Buffers;
using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sia;

internal sealed class EntityStorage
{
    private enum BackingOwnership : byte { Private, Rental, Borrowed }

    private static readonly ArrayPool<Entity> s_pool = ArrayPool<Entity>.Create();

    private Entity[] _items = [];
    private int _count;
    private int _version;
    private BackingOwnership _ownership;

    internal int Count => _count;
    internal int Capacity => _items.Length;

    internal Entity this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        set {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            _items[index] = value;
            _version++;
        }
    }

    internal void EnsureCapacity(int count, bool exactFresh = false)
    {
        if (count <= _items.Length) return;
        lock (this) {
            var items = _items;
            if (count <= items.Length) return;
            var capacity = Math.Max(4, items.Length);
            if (BitOperations.IsPow2(items.Length)) {
                capacity = (int)Math.Min(2L * items.Length, Array.MaxLength);
            }
            var exact = exactFresh && items.Length == 0 && count > 32 * 1024 && !BitOperations.IsPow2(count);
            var replacement = exact ? new Entity[count] : s_pool.Rent(Math.Max(capacity, count));
            items.AsSpan(0, _count).CopyTo(replacement);
            var returnPrevious = _ownership == BackingOwnership.Rental;
            _items = replacement;
            _ownership = exact ? BackingOwnership.Private : BackingOwnership.Rental;
            if (returnPrevious) s_pool.Return(items, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref Entity PrepareAppend(int index)
    {
        var items = _items;
        if ((uint)index < (uint)items.Length) return ref items[index];
        return ref GrowForAppend(index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ref Entity GrowForAppend(int index)
    {
        EnsureCapacity(index + 1);
        return ref _items[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CommitAppend(int index)
    {
        _count = index + 1;
        _version++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(Entity entity)
    {
        _version++;
        var index = _count;
        var items = _items;
        if ((uint)index < (uint)items.Length) {
            items[index] = entity;
            _count = index + 1;
        }
        else {
            AddWithGrowth(entity);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithGrowth(Entity entity)
    {
        var index = _count;
        EnsureCapacity(index + 1);
        _items[index] = entity;
        _count = index + 1;
    }

    internal void AddRange(EntityStorage source)
    {
        var end = checked(_count + source._count);
        EnsureCapacity(end);
        source.AsSpan().CopyTo(_items.AsSpan(_count));
        _count = end;
        _version++;
    }

    internal void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        var items = _items;
        var remaining = --_count - index;
        if (remaining != 0) items.AsSpan(index + 1, remaining).CopyTo(items.AsSpan(index));
        items[_count] = default;
        _version++;
    }

    internal void SetCount(int count, bool exactFresh = false)
    {
        EnsureCapacity(count, exactFresh);
        if (count < _count) _items.AsSpan(count, _count - count).Clear();
        _count = count;
        _version++;
    }

    internal void Clear() => SetCount(0);

    internal Span<Entity> AsSpan() => _items.AsSpan(0, _count);

    internal Span<Entity> BorrowSpan()
    {
        lock (this) {
            if (_items.Length != 0) _ownership = BackingOwnership.Borrowed;
            return AsSpan();
        }
    }

    internal void Dispose()
    {
        lock (this) {
            var items = _items;
            var ownership = _ownership;
            var count = _count;
            _items = [];
            _count = 0;
            _version++;
            _ownership = BackingOwnership.Private;
            if (ownership == BackingOwnership.Borrowed) {
                items.AsSpan(0, count).Clear();
            }
            else if (ownership == BackingOwnership.Rental) {
                s_pool.Return(items, clearArray: true);
            }
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator(EntityStorage owner) : IEnumerator<Entity>
    {
        private readonly int _version = owner._version;
        private int _index;
        private Entity _current;

        public readonly Entity Current => _current;
        readonly object IEnumerator.Current => _index == 0 || _index == owner._count + 1
            ? throw new InvalidOperationException("The enumerator does not have a current entity.")
            : _current;

        public bool MoveNext()
        {
            if (_version != owner._version) throw new InvalidOperationException("The entity collection changed.");
            if ((uint)_index < (uint)owner._count) {
                _current = owner._items[_index++];
                return true;
            }
            _index = owner._count + 1;
            _current = default;
            return false;
        }

        void IEnumerator.Reset()
        {
            if (_version != owner._version) throw new InvalidOperationException("The entity collection changed.");
            _index = 0;
            _current = default;
        }

        public readonly void Dispose() {}
    }
}
