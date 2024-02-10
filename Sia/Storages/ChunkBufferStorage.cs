namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class ChunkBufferStorage<T>
    where T : struct
{
    public static ChunkBufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<T>
        => new(buffer);
}
public class ChunkBufferStorage<T, TBuffer> : IStorage<T>
    where T : struct
    where TBuffer : IBuffer<T>
{
    private class Chunk
    {
        public int Index;
        public int Size;
        public bool IsAllocated;

        public Chunk? Previous;
        public Chunk? Next;
    }

    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    public int Count { get; private set; }

    public bool IsManaged => true;

    private readonly TBuffer _buffer;
    private readonly List<int> _versions = [];

    private Chunk _headChunk;
    private Chunk? _firstFreeChunk;

    private readonly Stack<Chunk> _chunkPool = new();

    public ChunkBufferStorage(TBuffer buffer)
    {
        if (buffer.Capacity <= 0) {
            throw new ArgumentException("Invalid capacity");
        }
        _buffer = buffer;
        _headChunk = new Chunk {
            Index = 0,
            Size = _buffer.Capacity,
            IsAllocated = false
        };
        _firstFreeChunk = _headChunk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint UnsafeAllocate(out int version)
    {
        var chunk = _firstFreeChunk
            ?? throw new IndexOutOfRangeException("Storage is full");
        int index = chunk.Index;
        _firstFreeChunk = AllocateFromFreeChunk(chunk).Next;

        ref var entry = ref _buffer.CreateRef(index);

        var versionCount = _versions.Count;
        if (index == versionCount) {
            _versions.Add(1);
            version = 1;
        }
        else {
            ref var versionRef = ref _versions.AsSpan()[index];
            versionRef = -versionRef + 1;
            version = versionRef;
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

        var chunk = FindChunkNodeByIndex(versionRef);
        var offset = index - chunk.Index;

        if (offset == 0) {
            chunk = FreeFirstChunkEntry(chunk);
        }
        else if (offset == chunk.Size - 1) {
            chunk = FreeLastChunkEntry(chunk);
        }
        else {
            UnsafeSplitChunk(chunk, offset);
            chunk.IsAllocated = false;
        }

        if (_firstFreeChunk == null
                || chunk.Index < _firstFreeChunk.Index) {
            _firstFreeChunk = chunk;
        }

        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        return index < _versions.Count && _versions[index] == version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Chunk CreateChunk()
        => _chunkPool.TryPop(out var chunk) ? chunk : new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveChunk(Chunk chunk)
    {
        var prevChunk = chunk.Previous;
        if (prevChunk == null) {
            _headChunk = chunk.Next
                ?? throw new InvalidOperationException("Cannot remove the only head chunk");
            _headChunk.Previous = null;
        }
        else {
            var nextChunk = chunk.Next;
            if (nextChunk != null) {
                prevChunk.Next = nextChunk;
                nextChunk.Previous = prevChunk;
            }
            else {
                prevChunk.Next = null;
            }
        }

        if (chunk == _firstFreeChunk) {
            _firstFreeChunk = null;
        }

        chunk.Next = null;
        chunk.Previous = null;

        _chunkPool.Push(chunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddChunkBefore(Chunk chunk, Chunk newChunk)
    {
        newChunk.Next = chunk;

        var prevChunk = chunk.Previous;
        chunk.Previous = newChunk;

        if (prevChunk == null) {
            _headChunk = newChunk;
        }
        else {
            prevChunk.Next = newChunk;
            newChunk.Previous = prevChunk;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddChunkAfter(Chunk chunk, Chunk newChunk)
    {
        newChunk.Previous = chunk;

        var nextChunk = chunk.Next;
        chunk.Next = newChunk;

        if (nextChunk != null) {
            newChunk.Next = nextChunk;
            nextChunk.Previous = newChunk;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Chunk FindChunkNodeByIndex(int index)
    {
        var prevChunk = _headChunk;
        var chunk = _headChunk.Next;

        while (chunk != null) {
            if (chunk.Index > index) {
                return prevChunk;
            }
            prevChunk = chunk;
            chunk = chunk.Next;
        }

        return prevChunk;
    }

    private Chunk AllocateFromFreeChunk(Chunk chunk)
    {
        if (chunk.Size == 1) {
            chunk.IsAllocated = true;

            var mergedNode = MergeWithPreviousChunk(chunk);
            return mergedNode != null
                ? (MergeWithNextChunk(mergedNode) ?? mergedNode)
                : chunk;
        }

        var prevChunk = chunk.Previous;
        if (prevChunk != null && prevChunk.IsAllocated) {
            prevChunk.Size++;
            chunk.Index++;
            chunk.Size--;
            return prevChunk;
        }

        var index = chunk.Index++;
        chunk.Size--;

        var newChunk = CreateChunk();
        newChunk.Index = index;
        newChunk.Size = 1;
        newChunk.IsAllocated = true;

        AddChunkBefore(chunk, newChunk);
        return newChunk;
    }

    private Chunk FreeFirstChunkEntry(Chunk chunk)
    {
        var prevChunk = chunk.Previous;
        if (prevChunk == null || prevChunk.IsAllocated) {
            if (chunk.Size == 1) {
                chunk.IsAllocated = false;
                return MergeWithNextChunk(chunk) ?? chunk;
            }
            else {
                var newChunk = CreateChunk();
                newChunk.Index = chunk.Index;
                newChunk.Size = 1;
                newChunk.IsAllocated = false;
                AddChunkBefore(chunk, newChunk);

                chunk.Index++;
                chunk.Size--;
                return newChunk;
            }
        }

        prevChunk.Size++;

        if (chunk.Size == 1) {
            RemoveChunk(chunk);
            return MergeWithNextChunk(prevChunk) ?? prevChunk;
        }
        else {
            chunk.Index++;
            chunk.Size--;
            return prevChunk;
        }
    }

    private Chunk FreeLastChunkEntry(Chunk chunk)
    {
        var nextChunk = chunk.Next;
        if (nextChunk == null || nextChunk.IsAllocated) {
            if (chunk.Size == 1) {
                chunk.IsAllocated = false;
                return MergeWithPreviousChunk(chunk) ?? chunk;
            }
            else {
                var newChunk = CreateChunk();
                newChunk.Index = chunk.Index + chunk.Size - 1;
                newChunk.Size = 1;
                newChunk.IsAllocated = false;
                AddChunkAfter(chunk, newChunk);

                chunk.Size--;
                return newChunk;
            }
        }

        nextChunk.Index--;
        nextChunk.Size++;

        if (chunk.Size == 1) {
            RemoveChunk(chunk);
            return MergeWithPreviousChunk(nextChunk) ?? nextChunk;
        }
        else {
            chunk.Size--;
            return nextChunk;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Chunk? MergeWithPreviousChunk(Chunk chunk)
    {
        var prevChunk = chunk.Previous;
        if (prevChunk == null) { return null; }

        if (prevChunk.IsAllocated != chunk.IsAllocated) {
            return null;
        }

        prevChunk.Size += chunk.Size;

        RemoveChunk(chunk);
        return prevChunk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Chunk? MergeWithNextChunk(Chunk chunk)
    {
        var nextChunk = chunk.Next;
        if (nextChunk == null) { return null; }

        if (nextChunk.IsAllocated != chunk.IsAllocated) {
            return null;
        }

        nextChunk.Index = chunk.Index;
        nextChunk.Size += chunk.Size;

        RemoveChunk(chunk);
        return nextChunk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnsafeSplitChunk(Chunk chunk, int position)
    {
        var newChunk = CreateChunk();
        newChunk.Index = chunk.Index;
        newChunk.Size = position;
        newChunk.IsAllocated = chunk.IsAllocated;
        AddChunkBefore(chunk, newChunk);

        newChunk = CreateChunk();
        newChunk.Index = chunk.Index + position + 1;
        newChunk.Size = chunk.Size - position - 1;
        newChunk.IsAllocated = chunk.IsAllocated;
        AddChunkAfter(chunk, newChunk);

        chunk.Index += position;
        chunk.Size = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(nint rawPointer, int version)
    {
        int index = (int)rawPointer;
        if (_versions[index] != version) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref _buffer.GetRef(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        if (Count == 0) { return; }

        int count = 0;
        for (int i = 0; i < _versions.Count; ++i) {
            int version = _versions[i];
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
        for (int i = 0; i < _versions.Count; ++i) {
            int version = _versions[i];
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
        for (int i = 0; i < _versions.Count; ++i) {
            int version = _versions[i];
            if (version > 0) {
                yield return (i, version);
                if (++count == Count) {
                    break;
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