using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    private sealed class GrowthState(T[] retainedArray)
    {
        // Under the growth lock, null identifies an unborrowed rental.
        // The fast borrow path matches this marker to its backing snapshot.
        public T[]? RetainedArray = retainedArray;
    }

    public int Count {
        get;
        set {
            if (value == field) return;
            if (value > _array.Length) Resize(value);
            field = value;
        }
    }

    private T[] _array = new T[initialCapacity];
    private GrowthState? _growthState;

    public ArrayBuffer() : this(0) {}

    internal void EnableGrowthPooling()
        => Interlocked.CompareExchange(ref _growthState, new GrowthState(_array), null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRow(int slot, in T value) => _array[slot] = value;

    private void Resize(int requiredCapacity)
    {
        var capacity = 8;
        while (capacity < requiredCapacity) { capacity *= 2; }

        var growthState = Volatile.Read(ref _growthState);
        if (growthState is null) {
            Array.Resize(ref _array, capacity);
            return;
        }

        lock (growthState) {
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
            var previousRental = growthState.RetainedArray is null ? previous : null;

            // Publish the marker before the new backing can be borrowed.
            Volatile.Write(ref growthState.RetainedArray, rental is null ? next : null);
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
        var growthState = Volatile.Read(ref _growthState);
        if (growthState is null || ReferenceEquals(array, Volatile.Read(ref growthState.RetainedArray))) {
            return array;
        }
        return RecordBorrow(growthState);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T[] RecordBorrow(GrowthState growthState)
    {
        lock (growthState) {
            var array = _array;
            Volatile.Write(ref growthState.RetainedArray, array);
            return array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
