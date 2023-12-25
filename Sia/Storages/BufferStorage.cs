using System.Collections;
using System.Runtime.CompilerServices;

namespace Sia
{

public struct BufferStorageEntry<T>
{
    public int Version;
    public T Value;
}

public sealed class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<BufferStorageEntry<T>>
        => new(buffer);
}

public class BufferStorage<T, TBuffer> : IStorage<T>
    where T : struct
    where TBuffer : IBuffer<BufferStorageEntry<T>>
{
    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    public int Count { get; private set; }
    public bool IsManaged => true;

    private int _firstFreeIndex;
    private readonly TBuffer _buffer;

    public BufferStorage(TBuffer buffer)
    {
        if (buffer.Capacity <= 0) {
            throw new ArgumentException("Buffer capacity must be greator than 0");
        }
        _buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint UnsafeAllocate(out int version)
    {
        var index = _firstFreeIndex;
        int capacity = _buffer.Capacity;

        if (index >= capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        while (++_firstFreeIndex < capacity && _buffer.GetRef(_firstFreeIndex).Version > 0) {}

        ref var entry = ref _buffer.GetRef(index);
        version = -entry.Version + 1;
        entry.Version = version;
        Count++;
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        ref var entry = ref _buffer.GetRef(index);

        if (entry.Version != version) {
            throw new ArgumentException("Invalid pointer");
        }

        entry.Version = -version;
        entry.Value = default;
        Count--;

        if (index < _firstFreeIndex) {
            _firstFreeIndex = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        if (index >= _buffer.Capacity || !_buffer.IsAllocated(index)) {
            return false;
        }
        return _buffer.GetRef(index).Version == version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(nint rawPointer, int version)
    {
        ref var entry = ref _buffer.GetRef((int)rawPointer);
        if (entry.Version != version) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref entry.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        if (Count == 0) { return; }

        int count = 0;
        for (int i = 0; i < _buffer.Capacity; ++i) {
            int version = _buffer.GetRef(i).Version;
            if (version > 0) {
                handler(i, version);
                if (++count >= Count) {
                    break;
                }
            }
        }
    }
    
    private readonly record struct IterationData<TData>(TData Data, StoragePointerHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        if (Count == 0) { return; }

        int count = 0;
        for (int i = 0; i < _buffer.Capacity; ++i) {
            int version = _buffer.GetRef(i).Version;
            if (version > 0) {
                handler(data, i, version);
                if (++count >= Count) {
                    break;
                }
            }
        }
    }

    public IEnumerator<(nint, int)> GetEnumerator()
    {
        if (Count == 0) { yield break; }

        int count = 0;
        for (int i = 0; i < _buffer.Capacity; ++i) {
            int version = _buffer.GetRef(i).Version;
            if (version > 0) {
                yield return (i, version);
                if (++count >= Count) {
                    yield break;
                }
            }
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