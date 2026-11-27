using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public sealed class ArrayBuffer<T> : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    // Keep one reclaimed rental per row type outside the finalizer thread's cache.
    private static T[]? s_reclaimedRental;

    private sealed class RetiredRental
    {
        private GCHandle _owner;
        private T[]? _array;

        public RetiredRental(ArrayBuffer<T> owner, T[] array)
        {
            _owner = GCHandle.Alloc(owner, GCHandleType.WeakTrackResurrection);
            _array = array;
        }

        public void Cancel()
        {
            lock (this) {
                _array = null;
                if (_owner.IsAllocated) _owner.Free();
            }
            GC.SuppressFinalize(this);
        }

        ~RetiredRental()
        {
            T[]? returned;
            lock (this) {
                if (!_owner.IsAllocated) return;
                // Another finalizer may resurrect the owner.
                if (_owner.Target is not null) {
                    GC.ReRegisterForFinalize(this);
                    return;
                }
                returned = _array;
                _array = null;
                _owner.Free();
            }
            if (returned is not null) Interlocked.Exchange(ref s_reclaimedRental, returned);
        }
    }

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

    // Under the growth lock, null identifies an unborrowed rental.
    private T[]? _retainedArray;
    private bool _exposed;
    private RetiredRental? _retiredRental;

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
        // A live buffer roots its pending retirement.
        if (!Volatile.Read(ref _exposed)) Volatile.Write(ref _exposed, true);
    }

    internal void ClearOwnedRows()
    {
        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null) {
            _array.AsSpan(0, Count).Clear();
            Count = 0;
            return;
        }

        lock (growthLock) {
            var rows = _array;
            var canRetire = !Volatile.Read(ref _exposed) && _retainedArray is null
                && _retiredRental is null;
            rows.AsSpan(0, canRetire ? rows.Length : Count).Clear();
            Count = 0;
            if (canRetire) {
                try {
                    _retiredRental = new RetiredRental(this, rows);
                }
                catch (OutOfMemoryException) {
                    // Keep the backing if scheduling fails.
                }
            }
        }
    }

    private void CancelRetiredRental()
    {
        if (_retiredRental is not { } retired) return;
        retired.Cancel();
        _retiredRental = null;
    }

    private static T[]? TryTakeReclaimedRental(int capacity)
    {
        var recovered = Volatile.Read(ref s_reclaimedRental);
        return recovered?.Length == capacity
            && ReferenceEquals(Interlocked.CompareExchange(ref s_reclaimedRental, null, recovered), recovered)
            ? recovered : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRow(int slot, in T value) => _array[slot] = value;

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
            CancelRetiredRental();
            var previous = _array;
            var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            T[]? rental = null;
            if (capacity is >= 1_024 and <= 131_072
                && (long)capacity * Unsafe.SizeOf<T>() <= 2 * 1024 * 1024) {
                var candidate = TryTakeReclaimedRental(capacity) ?? ArrayPool<T>.Shared.Rent(capacity);
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
            var previousRental = _retainedArray is null ? previous : null;

            // Publish the marker before the new backing can be borrowed.
            Volatile.Write(ref _retainedArray, rental is null ? next : null);
            Volatile.Write(ref _array, next);
            if (previousRental is not null) ArrayPool<T>.Shared.Return(previousRental, clearOnReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index) => ref BorrowArray()[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
    {
        var items = BorrowArray();
        return ref (uint)index < (uint)items.Length ? ref items[index] : ref Unsafe.NullRef<T>();
    }

    public Span<T> AsSpan() => BorrowArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T[] BorrowArray()
    {
        var array = Volatile.Read(ref _array);
        var growthLock = Volatile.Read(ref _growthLock);
        if (growthLock is null || ReferenceEquals(array, Volatile.Read(ref _retainedArray))) {
            return array;
        }
        return RecordBorrow(growthLock);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] RecordBorrow(object growthLock)
    {
        lock (growthLock) {
            CancelRetiredRental();
            var array = _array;
            Volatile.Write(ref _retainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
