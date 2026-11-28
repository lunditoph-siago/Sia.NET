using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T> : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    internal delegate void RowsHandler<TDestination, TState>(
        scoped Span<T> source, scoped Span<TDestination> target, scoped ref TState state)
        where TState : allows ref struct;

    public int Count {
        get;
        set {
            if (value == field) return;
            if (value > _array.Length) Resize(value);
            field = value;
        }
    }

    private T[] _array;
    private object? _poolLock;

    // A nonempty backing with a null marker has no escaped borrow.
    private T[]? _retainedArray;
    private bool _exposed;
    private int _rowBorrows;
    private int _detachedCapacity;
    private bool _returnPending;

    public ArrayBuffer(int initialCapacity)
    {
        _array = new T[initialCapacity];
        _retainedArray = _array;
    }

    public ArrayBuffer() : this(0) {}

    internal void EnablePooling()
        => Interlocked.CompareExchange(ref _poolLock, new object(), null);

    internal void MarkExposed()
    {
        if (!Volatile.Read(ref _exposed)) Volatile.Write(ref _exposed, true);
    }

    internal void HandleRows<TDestination, TState>(ArrayBuffer<TDestination> destination,
        scoped ref TState state, RowsHandler<TDestination, TState> handler)
        where TState : allows ref struct
    {
        var sourceRows = BorrowRows(out var sourceLock);
        try {
            var targetRows = destination.BorrowRows(out var targetLock);
            try {
                handler(sourceRows, targetRows, ref state);
            }
            finally {
                destination.ReleaseRows(targetLock);
            }
        }
        finally {
            ReleaseRows(sourceLock);
        }
    }

    private Span<T> BorrowRows(out object? poolLock)
    {
        var backing = Volatile.Read(ref _array);
        poolLock = Volatile.Read(ref _poolLock);
        if (poolLock is null || ReferenceEquals(backing, Volatile.Read(ref _retainedArray))) {
            poolLock = null;
            return backing.AsSpan(0, Count);
        }

        lock (poolLock) {
            var rows = _array.AsSpan(0, Count);
            _rowBorrows++;
            return rows;
        }
    }

    private void ReleaseRows(object? poolLock)
    {
        if (poolLock is null) return;

        T[]? rental = null;
        lock (poolLock) {
            if (--_rowBorrows == 0 && _returnPending) {
                _returnPending = false;
                if (Count == 0 && !Volatile.Read(ref _exposed) && _retainedArray is null) {
                    rental = _array;
                    PublishArray(Array.Empty<T>(), retain: false, detachedCapacity: rental.Length);
                }
            }
        }
        if (rental is not null) ReturnArray(rental);
    }

    internal void ClearRows()
    {
        var poolLock = Volatile.Read(ref _poolLock);
        if (poolLock is null) {
            _array.AsSpan(0, Count).Clear();
            Count = 0;
            return;
        }

        T[]? rental = null;
        lock (poolLock) {
            var rows = _array;
            var canReturn = rows.Length != 0 && !Volatile.Read(ref _exposed) && _retainedArray is null;
            if (canReturn && _rowBorrows == 0) {
                Count = 0;
                rental = rows;
                PublishArray(Array.Empty<T>(), retain: false, detachedCapacity: rental.Length);
            }
            else {
                rows.AsSpan(0, canReturn ? rows.Length : Count).Clear();
                Count = 0;
                _returnPending = canReturn;
            }
        }
        if (rental is not null) ReturnArray(rental);
    }

    private static void ReturnArray(T[] rows)
    {
        try {
            ArrayPool<T>.Shared.Return(rows, clearArray: true);
        }
        catch (OutOfMemoryException) {
            // The detached array can be collected.
        }
    }

    private T[] PublishArray(T[] array, bool retain = true, int detachedCapacity = 0)
    {
        _detachedCapacity = detachedCapacity;
        // Publish the marker before the new backing can be borrowed.
        Volatile.Write(ref _retainedArray, retain ? array : null);
        Volatile.Write(ref _array, array);
        return array;
    }

    // The mutation owner uses this alias for immediate row operations.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetOwnedRef(int slot) => ref _array[slot];

    internal T ReadRow(int slot)
    {
        var array = Volatile.Read(ref _array);
        var poolLock = Volatile.Read(ref _poolLock);
        if (poolLock is null || ReferenceEquals(array, Volatile.Read(ref _retainedArray))) {
            return array[slot];
        }

        lock (poolLock) {
            if ((uint)slot < (uint)_detachedCapacity) return default!;
            return _array[slot];
        }
    }

    private void Resize(int requiredCapacity)
    {
        var capacity = 8;
        while (capacity < requiredCapacity) { capacity *= 2; }

        var poolLock = Volatile.Read(ref _poolLock);
        if (poolLock is null) {
            var next = _array;
            Array.Resize(ref next, capacity);
            PublishArray(next);
            return;
        }

        lock (poolLock) {
            if (_detachedCapacity != 0) {
                PublishArray(new T[Math.Max(_detachedCapacity, capacity)]);
                return;
            }

            var previous = _array;
            var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            T[]? rental = null;
            if (capacity is >= 1_024 and <= 131_072
                && (long)capacity * Unsafe.SizeOf<T>() <= 2 * 1024 * 1024) {
                var candidate = ArrayPool<T>.Shared.Rent(capacity);
                if (candidate.Length == capacity) {
                    candidate.AsSpan(previous.Length).Clear();
                    rental = candidate;
                }
                else {
                    ArrayPool<T>.Shared.Return(candidate, clearOnReturn);
                }
            }

            var next = rental ?? new T[capacity];
            // AsSpan exposes the full capacity, including rows past Count.
            previous.CopyTo(next, 0);
            var previousRental = _rowBorrows == 0 && _retainedArray is null ? previous : null;
            _returnPending = false;

            PublishArray(next, retain: rental is null);
            if (previousRental is not null) ArrayPool<T>.Shared.Return(previousRental, clearOnReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index) => ref BorrowArray((long)(uint)index + 1)[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
    {
        var array = BorrowArray((long)(uint)index + 1);
        return ref (uint)index < (uint)array.Length ? ref array[index] : ref Unsafe.NullRef<T>();
    }

    public Span<T> AsSpan() => BorrowArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T[] BorrowArray(long requiredLength = 0)
    {
        var array = Volatile.Read(ref _array);
        var poolLock = Volatile.Read(ref _poolLock);
        if (poolLock is null || ReferenceEquals(array, Volatile.Read(ref _retainedArray))) {
            return array;
        }
        return BorrowLocked(poolLock, requiredLength);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] BorrowLocked(object poolLock, long requiredLength)
    {
        lock (poolLock) {
            if (_detachedCapacity != 0) {
                return requiredLength <= _detachedCapacity
                    ? PublishArray(new T[_detachedCapacity]) : Array.Empty<T>();
            }

            var array = _array;
            if (requiredLength <= array.Length) Volatile.Write(ref _retainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
