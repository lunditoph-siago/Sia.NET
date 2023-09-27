using System.Runtime.CompilerServices;

namespace Sia
{

public struct BufferStorageEntry<T>
{
    public int Index;
    public T Value;
}

public sealed class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<BufferStorageEntry<T>>
        => new(buffer);
}

public class BufferStorage<T, TBuffer>
    : Internal.BufferStorage<T, WrappedBuffer<BufferStorageEntry<T>, TBuffer>>
    where T : struct
    where TBuffer : IBuffer<BufferStorageEntry<T>>
{
    public BufferStorage(TBuffer buffer)
        : base(new(buffer))
    {
    }
}

namespace Internal
{
    public class BufferStorage<T, TBuffer> : IStorage<T>
        where T : struct
        where TBuffer : IBuffer<BufferStorageEntry<T>>
    {
        private struct Chunk
        {
            public int Index;
            public int Size;
            public bool IsAllocated;
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
        private LinkedList<Chunk> _chunks = new();
        private LinkedListNode<Chunk>? _firstFreeChunkNode;

        internal BufferStorage(TBuffer buffer)
        {
            if (buffer.Capacity <= 0) {
                throw new ArgumentException("Invalid capacity");
            }
            _buffer = buffer;
            _firstFreeChunkNode = _chunks.AddLast(new Chunk {
                Index = 0,
                Size = _buffer.Capacity,
                IsAllocated = false
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long UnsafeAllocate()
        {
            var chunkNode = _firstFreeChunkNode
                ?? throw new IndexOutOfRangeException("Storage is full");
            int index = chunkNode.ValueRef.Index;
            _firstFreeChunkNode = AllocateFromFreeChunk(chunkNode).Next;

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

            var chunkNode = FindChunkNodeByIndex(entry.Index);
            ref var chunk = ref chunkNode.ValueRef;

            var offset = entry.Index - chunk.Index;
            if (offset == 0) {
                chunkNode = FreeFirstChunkEntry(chunkNode);
            }
            else if (offset == chunk.Size - 1) {
                chunkNode = FreeLastChunkEntry(chunkNode);
            }
            else {
                UnsafeSplitChunk(chunkNode, offset);
                chunkNode.ValueRef.IsAllocated = false;
            }

            if (_firstFreeChunkNode == null
                    || _firstFreeChunkNode.List == null
                    || chunkNode.ValueRef.Index < _firstFreeChunkNode.ValueRef.Index) {
                _firstFreeChunkNode = chunkNode;
            }

            _buffer.Remove(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LinkedListNode<Chunk> FindChunkNodeByIndex(int index)
        {
            var node = _chunks.First!.Next;

            while (node != null) {
                if (node.ValueRef.Index > index) {
                    return node.Previous!;
                }
                node = node.Next;
            }

            return _chunks.Last!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LinkedListNode<Chunk> AllocateFromFreeChunk(LinkedListNode<Chunk> chunkNode)
        {
            ref var chunk = ref chunkNode.ValueRef;

            if (chunk.Size == 1) {
                chunk.IsAllocated = true;

                var mergedNode = MergeWithPreviousChunk(chunkNode);
                return mergedNode != null
                    ? (MergeWithNextChunk(mergedNode) ?? mergedNode)
                    : chunkNode;
            }

            var prevChunkNode = chunkNode.Previous;
            if (prevChunkNode != null) {
                ref var prevChunk = ref prevChunkNode.ValueRef;
                if (prevChunk.IsAllocated) {
                    prevChunk.Size++;
                    chunk.Index++;
                    chunk.Size--;
                    return prevChunkNode;
                }
            }

            var index = chunk.Index++;
            chunk.Size--;

            return _chunks.AddBefore(chunkNode, new Chunk {
                Index = index,
                Size = 1,
                IsAllocated = true
            });
        }

        private LinkedListNode<Chunk> FreeFirstChunkEntry(LinkedListNode<Chunk> chunkNode)
        {
            ref var chunk = ref chunkNode.ValueRef;

            var prevChunkNode = chunkNode.Previous;
            if (prevChunkNode == null || prevChunkNode.ValueRef.IsAllocated) {
                if (chunk.Size == 1) {
                    chunk.IsAllocated = false;
                    return MergeWithNextChunk(chunkNode) ?? chunkNode;
                }
                else {
                    var freeChunkNode = _chunks.AddBefore(chunkNode, new Chunk {
                        Index = chunk.Index,
                        Size = 1,
                        IsAllocated = false
                    });
                    chunk.Index++;
                    chunk.Size--;
                    return freeChunkNode;
                }
            }

            ref var prevChunk = ref prevChunkNode.ValueRef;
            prevChunk.Size++;

            if (chunk.Size == 1) {
                _chunks.Remove(chunkNode);
                return MergeWithNextChunk(prevChunkNode) ?? prevChunkNode;
            }
            else {
                chunk.Index++;
                chunk.Size--;
                return prevChunkNode;
            }
        }

        private LinkedListNode<Chunk> FreeLastChunkEntry(LinkedListNode<Chunk> chunkNode)
        {
            ref var chunk = ref chunkNode.ValueRef;

            var nextChunkNode = chunkNode.Next;
            if (nextChunkNode == null || nextChunkNode.ValueRef.IsAllocated) {
                if (chunk.Size == 1) {
                    chunk.IsAllocated = false;
                    return MergeWithPreviousChunk(chunkNode) ?? chunkNode;
                }
                else {
                    var freeChunkNode = _chunks.AddAfter(chunkNode, new Chunk {
                        Index = chunk.Index + chunk.Size - 1,
                        Size = 1,
                        IsAllocated = false
                    });
                    chunk.Size--;
                    return freeChunkNode;
                }
            }

            ref var nextChunk = ref nextChunkNode.ValueRef;
            nextChunk.Index--;
            nextChunk.Size++;

            if (chunk.Size == 1) {
                _chunks.Remove(chunkNode);
                return MergeWithPreviousChunk(nextChunkNode) ?? nextChunkNode;
            }
            else {
                chunk.Size--;
                return nextChunkNode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LinkedListNode<Chunk>? MergeWithPreviousChunk(LinkedListNode<Chunk> chunkNode)
        {
            var prevChunkNode = chunkNode.Previous;
            if (prevChunkNode == null) { return null; }

            ref var chunk = ref chunkNode.ValueRef;
            ref var prevChunk = ref prevChunkNode.ValueRef;

            if (prevChunk.IsAllocated != chunk.IsAllocated) {
                return null;
            }

            prevChunk.Size += chunk.Size;

            _chunks.Remove(chunkNode);
            return prevChunkNode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LinkedListNode<Chunk>? MergeWithNextChunk(LinkedListNode<Chunk> chunkNode)
        {
            var nextChunkNode = chunkNode.Next;
            if (nextChunkNode == null) { return null; }

            ref var chunk = ref chunkNode.ValueRef;
            ref var nextChunk = ref nextChunkNode.ValueRef;

            if (nextChunk.IsAllocated != chunk.IsAllocated) {
                return null;
            }

            nextChunk.Index = chunk.Index;
            nextChunk.Size += chunk.Size;

            _chunks.Remove(chunkNode);
            return nextChunkNode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UnsafeSplitChunk(LinkedListNode<Chunk> chunkNode, int position)
        {
            ref var chunk = ref chunkNode.ValueRef;

            _chunks.AddBefore(chunkNode, new Chunk {
                Index = chunk.Index,
                Size = position,
                IsAllocated = chunk.IsAllocated
            });

            _chunks.AddAfter(chunkNode, new Chunk {
                Index = chunk.Index + position + 1,
                Size = chunk.Size - position - 1,
                IsAllocated = chunk.IsAllocated
            });

            chunk.Index += position;
            chunk.Size = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T UnsafeGetRef(long rawPointer)
            => ref _buffer.GetValueRefOrNullRef((int)rawPointer).Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateAllocated(StoragePointerHandler handler)
        {
            var node = _chunks.First;

            while (node != null) {
                ref var nodeRef = ref node.ValueRef;
                if (nodeRef.IsAllocated) {
                    var lastIndex = nodeRef.Index + nodeRef.Size;
                    for (int i = nodeRef.Index; i != lastIndex; ++i) {
                        handler(i);
                    }
                }
                node = node.Next;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
        {
            var node = _chunks.First;

            while (node != null) {
                ref var nodeRef = ref node.ValueRef;
                if (nodeRef.IsAllocated) {
                    var lastIndex = nodeRef.Index + nodeRef.Size;
                    for (int i = nodeRef.Index; i != lastIndex; ++i) {
                        handler(data, i);
                    }
                }
                node = node.Next;
            }
        }
        
        public void Dispose()
        {
            _buffer.Dispose();
            _chunks = null!;
            GC.SuppressFinalize(this);
        }
    }
}

}