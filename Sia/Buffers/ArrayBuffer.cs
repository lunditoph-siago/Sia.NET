using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    private enum BackingOwnership : byte { Private, Rental, BorrowedRental }

    // Keep at most one finalized rental per row type. A Shared-pool return on the
    // finalizer thread alone would normally stay in that thread's local bucket.
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
                // An unrelated finalizer can still make the host reachable again.
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

    private int _count;
    private bool _reuseGrowth;
    private BackingOwnership _ownership;
    private bool _exposed;
    private RetiredRental? _retiredRental;
    private T[]? _borrowedArray;
    private readonly object _growthLock = new();

    public int Count {
        get => _count;
        set {
            if (value == _count) return;
            if (value > _array.Length) Resize(CalculateArraySize(value));
            _count = value;
        }
    }

    private T[] _array = new T[initialCapacity];

    public ArrayBuffer() : this(0) {}

    internal void ReuseUnborrowedGrowthArrays() => _reuseGrowth = true;

    internal void RecordExposure()
    {
        lock (_growthLock) {
            _exposed = true;
            CancelRetiredRental();
        }
    }

    internal void ClearOwnedRows()
    {
        lock (_growthLock) {
            var rows = _array;
            var canRetire = _reuseGrowth && !_exposed
                && _ownership == BackingOwnership.Rental && _retiredRental is null;
            rows.AsSpan(0, canRetire ? rows.Length : _count).Clear();
            _count = 0;
            if (canRetire) {
                try {
                    _retiredRental = new RetiredRental(this, rows);
                }
                catch (OutOfMemoryException) {
                    // The exact same backing remains visible even if the return cannot be scheduled.
                }
            }
        }
    }

    private void CancelRetiredRental()
    {
        _retiredRental?.Cancel();
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

    private void Resize(int capacity)
    {
        if (!_reuseGrowth) {
            Array.Resize(ref _array, capacity);
            return;
        }

        lock (_growthLock) {
            CancelRetiredRental();
            var previous = _array;
            var pool = ArrayPool<T>.Shared;
            T[]? next = null;
            if (capacity is >= 1024 and <= 131_072 && (capacity & (capacity - 1)) == 0
                && (long)capacity * Unsafe.SizeOf<T>() <= 2 * 1024 * 1024) {
                var rented = TryTakeReclaimedRental(capacity) ?? pool.Rent(capacity);
                if (rented.Length == capacity) {
                    rented.AsSpan(previous.Length).Clear();
                    next = rented;
                }
                else {
                    pool.Return(rented, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                }
            }

            var ownership = next is not null ? BackingOwnership.Rental : BackingOwnership.Private;
            next ??= new T[capacity];
            previous.CopyTo(next, 0);
            var returnsPrevious = _ownership == BackingOwnership.Rental;
            _ownership = ownership;
            Volatile.Write(ref _borrowedArray, null);
            Volatile.Write(ref _array, next);
            if (returnsPrevious) {
                pool.Return(previous, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateArraySize(int requiredCapacity)
    {
        var size = 8;
        while (size < requiredCapacity) { size *= 2; }
        return size;
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
        return !_reuseGrowth || ReferenceEquals(array, Volatile.Read(ref _borrowedArray))
            ? array : RecordBorrow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] RecordBorrow()
    {
        lock (_growthLock) {
            CancelRetiredRental();
            var array = _array;
            if (_ownership == BackingOwnership.Rental) _ownership = BackingOwnership.BorrowedRental;
            Volatile.Write(ref _borrowedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
