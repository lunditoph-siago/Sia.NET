using System.Collections;
using System.Runtime.CompilerServices;

namespace Sia
{

public sealed class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<T>
        => new(buffer);
}

public class BufferStorage<T, TBuffer> : IStorage<T>
    where T : struct
    where TBuffer : IBuffer<T>
{
    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Count;
    }

    public int PointerValidBits => 32;
    public bool IsManaged => true;

    public int PageSize { get; }

    private int _firstFreeIndex;
    private readonly TBuffer _buffer;

    public BufferStorage(TBuffer buffer)
    {
        if (buffer.Capacity <= 0) {
            throw new ArgumentException("Invalid capacity");
        }
        _buffer = buffer;
    }

    ~BufferStorage()
    {
        _buffer.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate()
    {
        int capacity = _buffer.Capacity;
        if (_buffer.Count == capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        var index = _firstFreeIndex;
        while (++_firstFreeIndex < capacity && _buffer.Contains(_firstFreeIndex)) {}

        _buffer.GetOrAddValueRef(index, out bool _);
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        int index = (int)rawPointer;
        if (!_buffer.Remove(index)) {
            throw new ArgumentException("Invalid pointer");
        }
        if (index < _firstFreeIndex) {
            _firstFreeIndex = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
    {
        ref var value = ref _buffer.GetValueRefOrNullRef((int)rawPointer);
        if (Unsafe.IsNullRef(ref value)) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
        => _buffer.IterateAllocated(handler,
            (in StoragePointerHandler handler, int index) => handler(index));
    
    private readonly record struct IterationData<TData>(TData Data, StoragePointerHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
        => _buffer.IterateAllocated(new(data, handler),
            (in IterationData<TData> data, int index) => data.Handler(data.Data, index));

    public IEnumerator<long> GetEnumerator()
    {
        foreach (var index in _buffer) {
            yield return index;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public void Dispose()
    {
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}

}