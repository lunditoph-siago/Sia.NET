using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

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

    public ArrayBuffer() : this(0) {}

    internal void EnableGrowthPooling()
        => Interlocked.CompareExchange(ref _growthPool, new GrowthPool(_array), null);

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

        var rental = ArrayPool<T>.Shared.Rent(capacity);
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
            var array = _array;
            Volatile.Write(ref growthPool.RetainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
