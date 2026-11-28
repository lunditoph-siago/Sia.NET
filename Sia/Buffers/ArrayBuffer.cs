using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T> : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    internal delegate void RowsAction<TDestination, TState>(
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
    private object? _growthLock;

    // A nonempty backing with a null marker has no escaped borrow.
    private T[]? _retainedArray;
    private bool _exposed;
    private int _activeScopedBorrows;
    private int _detachedCapacity;
    private bool _returnAfterScope;

    public ArrayBuffer(int initialCapacity)
    {
        _array = new T[initialCapacity];
        _retainedArray = _array;
    }

    public ArrayBuffer() : this(0) {}

    internal void EnableGrowthPooling()
        => Interlocked.CompareExchange(ref _growthLock, new object(), null);

    internal void RecordExposure()
    {
        if (!Volatile.Read(ref _exposed)) Volatile.Write(ref _exposed, true);
    }

    internal void WithRows<TDestination, TState>(ArrayBuffer<TDestination> destination,
        scoped ref TState state, RowsAction<TDestination, TState> action)
        where TState : allows ref struct
    {
        var sourceRows = BeginRows(out var sourceLock);
        try {
            var targetRows = destination.BeginRows(out var targetLock);
            try {
                action(sourceRows, targetRows, ref state);
            }
            finally {
                destination.EndRows(targetLock);
            }
        }
        finally {
            EndRows(sourceLock);
        }
    }

    private Span<T> BeginRows(out object? growthLock)
    {
        var backing = Volatile.Read(ref _array);
        growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null || ReferenceEquals(backing, Volatile.Read(ref _retainedArray))) {
            growthLock = null;
            return backing.AsSpan(0, Count);
        }

        lock (growthLock) {
            var rows = _array.AsSpan(0, Count);
            _activeScopedBorrows++;
            return rows;
        }
    }

    private void EndRows(object? growthLock)
    {
        if (growthLock is null) return;

        T[]? rental = null;
        lock (growthLock) {
            if (--_activeScopedBorrows == 0 && _returnAfterScope) {
                _returnAfterScope = false;
                if (Count == 0 && !Volatile.Read(ref _exposed) && _retainedArray is null) {
                    rental = DetachRows();
                }
            }
        }
        if (rental is not null) ReturnRows(rental);
    }

    internal void ClearOwnedRows()
    {
        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null) {
            _array.AsSpan(0, Count).Clear();
            Count = 0;
            return;
        }

        T[]? rental = null;
        lock (growthLock) {
            var rows = _array;
            var canReturn = rows.Length != 0 && !Volatile.Read(ref _exposed) && _retainedArray is null;
            if (canReturn && _activeScopedBorrows == 0) {
                Count = 0;
                rental = DetachRows();
            }
            else {
                rows.AsSpan(0, canReturn ? rows.Length : Count).Clear();
                Count = 0;
                _returnAfterScope = canReturn;
            }
        }
        if (rental is not null) ReturnRows(rental);
    }

    private T[] DetachRows()
    {
        // Preserve the capacity for callers that reach Buffer after disposal.
        var rows = _array;
        _detachedCapacity = rows.Length;
        Volatile.Write(ref _array, Array.Empty<T>());
        return rows;
    }

    private static void ReturnRows(T[] rows)
    {
        try {
            ArrayPool<T>.Shared.Return(rows, clearArray: true);
        }
        catch (OutOfMemoryException) {
            // The detached array can be collected.
        }
    }

    private T[] RestoreDetachedArray(int capacity)
    {
        var array = new T[capacity];
        _detachedCapacity = 0;
        Volatile.Write(ref _retainedArray, array);
        Volatile.Write(ref _array, array);
        return array;
    }

    // The mutation owner uses this alias for immediate row operations.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetOwnedRow(int slot) => ref _array[slot];

    internal T ReadRow(int slot)
    {
        var array = Volatile.Read(ref _array);
        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null || ReferenceEquals(array, Volatile.Read(ref _retainedArray))) {
            return array[slot];
        }

        lock (growthLock) {
            if ((uint)slot < (uint)_detachedCapacity) return default!;
            return _array[slot];
        }
    }

    private void Resize(int requiredCapacity)
    {
        var capacity = 8;
        while (capacity < requiredCapacity) { capacity *= 2; }

        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null) {
            var next = _array;
            Array.Resize(ref next, capacity);
            Volatile.Write(ref _retainedArray, next);
            Volatile.Write(ref _array, next);
            return;
        }

        lock (growthLock) {
            if (_detachedCapacity != 0) {
                RestoreDetachedArray(Math.Max(_detachedCapacity, capacity));
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
            var previousRental = _activeScopedBorrows == 0 && _retainedArray is null ? previous : null;
            _returnAfterScope = false;

            // Publish the marker before the new backing can be borrowed.
            Volatile.Write(ref _retainedArray, rental is null ? next : null);
            Volatile.Write(ref _array, next);
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
        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null || ReferenceEquals(array, Volatile.Read(ref _retainedArray))) {
            return array;
        }
        return RecordBorrow(growthLock, requiredLength);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] RecordBorrow(object growthLock, long requiredLength)
    {
        lock (growthLock) {
            if (_detachedCapacity != 0) {
                return requiredLength <= _detachedCapacity
                    ? RestoreDetachedArray(_detachedCapacity) : Array.Empty<T>();
            }

            var array = _array;
            if (requiredLength <= array.Length) Volatile.Write(ref _retainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
