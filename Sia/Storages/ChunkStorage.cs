using System.Runtime.CompilerServices;

namespace Sia
{

public struct ChunkStorageEntry<T>
{
    public int Index;
    public T Value;
}

public sealed class ChunkStorage<T>
    where T : struct
{
    public static ChunkStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<ChunkStorageEntry<T>>
        => new(buffer);
}

public class ChunkStorage<T, TBuffer>
    : Internal.ChunkStorage<T, WrappedBuffer<ChunkStorageEntry<T>, TBuffer>>
    where T : struct
    where TBuffer : IBuffer<ChunkStorageEntry<T>>
{
    public ChunkStorage(TBuffer buffer)
        : base(new(buffer))
    {
    }
}

namespace Internal
{
    public class ChunkStorage<T, TBuffer> : IStorage<T>
        where T : struct
        where TBuffer : IBuffer<ChunkStorageEntry<T>>
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

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Count;
        }

        public int PointerValidBits => 32;
        public bool IsManaged => true;

        public int PageSize { get; }

        private readonly TBuffer _buffer;
        private Chunk _headChunk;
        private Chunk? _firstFreeChunk;

        private readonly Stack<Chunk> _chunkPool = new();

        internal ChunkStorage(TBuffer buffer)
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

        ~ChunkStorage()
        {
            _buffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long UnsafeAllocate()
        {
            var chunk = _firstFreeChunk
                ?? throw new IndexOutOfRangeException("Storage is full");
            int index = chunk.Index;
            _firstFreeChunk = AllocateFromFreeChunk(chunk).Next;

            ref var entry = ref _buffer.GetOrAddValueRef(index, out bool _);
            entry.Index = index;
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeRelease(long rawPointer)
        {
            int index = (int)rawPointer;

            ref var entry = ref _buffer.GetValueRefOrNullRef(index);
            if (Unsafe.IsNullRef(ref entry)) {
                throw new ArgumentException("Invalid pointer");
            }

            var chunk = FindChunkNodeByIndex(entry.Index);

            var offset = entry.Index - chunk.Index;
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

            _buffer.Remove(index);
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
        public ref T UnsafeGetRef(long rawPointer)
            => ref _buffer.GetValueRefOrNullRef((int)rawPointer).Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateAllocated(StoragePointerHandler handler)
            => _buffer.IterateAllocated(handler,
                (in StoragePointerHandler handler, int index) => handler(index));
        
        private record struct IterationData<TData>(TData Data, StoragePointerHandler<TData> Handler);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
            => _buffer.IterateAllocated(new(data, handler),
                (in IterationData<TData> data, int index) => data.Handler(data.Data, index));
        
        public void Dispose()
        {
            _buffer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

}