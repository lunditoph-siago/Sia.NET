using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

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

    private sealed class GrowthPool(T[] retainedArray)
    {
        // A null marker permits returning the current rental to the pool.
        public T[]? RetainedArray = retainedArray;
    }

    public int Count {
        get;
        set {
            if (value == field) return;
            if (value > _array.Length) Resize(CalculateArraySize(value));
            field = value;
        }
    }

    private T[] _array = new T[initialCapacity];
    private GrowthPool? _growthPool;
    private bool _exposed;
    private RetiredRental? _retiredRental;

    public ArrayBuffer() : this(0) {}

    internal void EnableGrowthPooling()
        => Interlocked.CompareExchange(ref _growthPool, new GrowthPool(_array), null);

    internal void RecordExposure()
    {
        Volatile.Write(ref _exposed, true);
        var growthPool = Volatile.Read(ref _growthPool);
        if (growthPool is null) return;

        lock (growthPool) {
            CancelRetiredRental();
        }
    }

    internal void ClearOwnedRows()
    {
        var growthPool = Volatile.Read(ref _growthPool);
        if (growthPool is null) {
            _array.AsSpan(0, Count).Clear();
            Count = 0;
            return;
        }

        lock (growthPool) {
            var rows = _array;
            var canRetire = !Volatile.Read(ref _exposed) && growthPool.RetainedArray is null
                && _retiredRental is null;
            rows.AsSpan(0, canRetire ? rows.Length : Count).Clear();
            Count = 0;
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
        var growthPool = Volatile.Read(ref _growthPool);
        if (growthPool is null) {
            Array.Resize(ref _array, capacity);
            return;
        }

        lock (growthPool) {
            CancelRetiredRental();
            var previous = _array;
            var rental = TryRentGrowthArray(capacity);
            var next = rental ?? new T[capacity];
            if (rental is not null) rental.AsSpan(previous.Length).Clear();

            previous.CopyTo(next, 0);
            var returnPrevious = !ReferenceEquals(previous, growthPool.RetainedArray);
            Volatile.Write(ref growthPool.RetainedArray, rental is null ? next : null);
            Volatile.Write(ref _array, next);
            if (returnPrevious) ReturnRental(previous);
        }
    }

    private static T[]? TryRentGrowthArray(int capacity)
    {
        if (capacity is < 1_024 or > 131_072
            || (long)capacity * Unsafe.SizeOf<T>() > 2 * 1024 * 1024) return null;

        var rental = TryTakeReclaimedRental(capacity) ?? ArrayPool<T>.Shared.Rent(capacity);
        if (rental.Length == capacity) return rental;
        ReturnRental(rental);
        return null;
    }

    private static void ReturnRental(T[] array)
        => ArrayPool<T>.Shared.Return(array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

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
        var growthPool = Volatile.Read(ref _growthPool);
        return growthPool is null || ReferenceEquals(array, Volatile.Read(ref growthPool.RetainedArray))
            ? array : RecordBorrow(growthPool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] RecordBorrow(GrowthPool growthPool)
    {
        lock (growthPool) {
            CancelRetiredRental();
            var array = _array;
            Volatile.Write(ref growthPool.RetainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
