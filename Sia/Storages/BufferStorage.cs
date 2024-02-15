namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<T>
        => new(buffer);
}

public class BufferStorage<T, TBuffer>(TBuffer buffer) : IStorage<T>
    where T : struct
    where TBuffer : IBuffer<T>
{
    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    public int Count { get; private set; }
    public bool IsManaged => true;

    private int _firstFreeIndex;
    private readonly TBuffer _buffer = buffer;
    private readonly List<int> _versions = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint UnsafeAllocate(out int version)
    {
        var index = _firstFreeIndex;
        int capacity = _buffer.Capacity;

        if (index >= capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        _buffer.CreateRef(index);

        var versionCount = _versions.Count;
        if (index == versionCount) {
            _versions.Add(1);
            version = 1;
            _firstFreeIndex++;
        }
        else {
            ref var versionRef = ref _versions.AsSpan()[index];
            versionRef = -versionRef + 1;
            version = versionRef;
            while (++_firstFreeIndex < versionCount && _versions[_firstFreeIndex] > 0) {}
        }

        Count++;
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        ref var versionRef = ref _versions.AsSpan()[index];

        if (versionRef != version) {
            throw new ArgumentException("Invalid pointer");
        }

        _buffer.Release(index);
        versionRef = -versionRef;

        Count--;

        if (index < _firstFreeIndex) {
            _firstFreeIndex = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        return index < _versions.Count && _versions[index] == version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(nint rawPointer, int version)
    {
        var index = (int)rawPointer;
        if (_versions[index] != version) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref _buffer.GetRef(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        for (int i = 0, entityAcc = 0; entityAcc < Count; ++i) {
            int version = _versions[i];
            if (version > 0) {
                handler(i, version);
                entityAcc++;
            }
        }
    }
    
    private readonly record struct IterationData<TData>(TData Data, StoragePointerHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        for (int i = 0, entityAcc = 0; entityAcc < Count; ++i) {
            int version = _versions[i];
            if (version > 0) {
                handler(data, i, version);
                entityAcc++;
            }
        }
    }

    public IEnumerator<(nint, int)> GetEnumerator()
    {
        for (int i = 0, entityAcc = 0; entityAcc < Count; ++i) {
            int version = _versions[i];
            if (version > 0) {
                yield return (i, version);
                entityAcc++;
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