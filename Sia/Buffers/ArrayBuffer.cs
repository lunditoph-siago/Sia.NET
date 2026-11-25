using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    private enum BackingOwnership : byte { Private, Rental, BorrowedRental }

    private int _count;
    private bool _reuseGrowth;
    private BackingOwnership _ownership;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRow(int slot, in T value) => _array[slot] = value;

    private void Resize(int capacity)
    {
        if (!_reuseGrowth) {
            Array.Resize(ref _array, capacity);
            return;
        }

        lock (_growthLock) {
            var previous = _array;
            var pool = ArrayPool<T>.Shared;
            T[]? next = null;
            if (capacity is >= 1024 and <= 131_072 && (capacity & (capacity - 1)) == 0
                && (long)capacity * Unsafe.SizeOf<T>() <= 2 * 1024 * 1024) {
                var rented = pool.Rent(capacity);
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
            var array = _array;
            if (_ownership == BackingOwnership.Rental) _ownership = BackingOwnership.BorrowedRental;
            Volatile.Write(ref _borrowedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
