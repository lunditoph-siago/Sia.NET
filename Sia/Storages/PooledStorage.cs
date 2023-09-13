using System.Runtime.CompilerServices;

namespace Sia
{

public static class PooledStorage<T>
    where T : struct
{
    public static PooledStorage<T, TStorage> Create<TStorage>(
        TStorage storage, int poolSize = int.MaxValue)
        where TStorage : class, IStorage<T>
        => new(storage, poolSize);
}

public class PooledStorage<T, TStorage>
    : Internal.PooledStorage<T, StorageWrapper<T, TStorage>>
    where T : struct
    where TStorage : class, IStorage<T>
{
    public PooledStorage(TStorage innerStorage, int poolSize = int.MaxValue)
        : base(new(innerStorage), poolSize)
    {
    }
}

namespace Internal
{
    public class PooledStorage<T, TStorage> : IStorage<T>
        where T : struct
        where TStorage : IStorage<T>
    {
        public TStorage InnerStorage { get; }
        public int PoolSize { get; set; }

        public int Capacity {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InnerStorage.Capacity;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InnerStorage.Count;
        }

        public int PointerValidBits {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InnerStorage.PointerValidBits;
        }

        public bool IsManaged {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InnerStorage.IsManaged;
        }

        private Stack<long> _pooled = new();

        internal PooledStorage(TStorage innerStorage, int poolSize)
        {
            InnerStorage = innerStorage;
            PoolSize = poolSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pointer<T> Allocate()
        {
            if (!_pooled.TryPop(out var ptr)) {
                ptr = InnerStorage.Allocate().Raw;
            }
            return new(ptr, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pointer<T> Allocate(in T initial)
        {
            if (_pooled.TryPop(out var ptr)) {
                InnerStorage.UnsafeGetRef(ptr) = initial;
            }
            else {
                ptr = InnerStorage.Allocate(initial).Raw;
            }
            return new(ptr, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeRelease(long rawPointer)
        {
            if (_pooled.Count < PoolSize) {
                InnerStorage.UnsafeGetRef(rawPointer) = default;
                _pooled.Push(rawPointer);
            }
            else {
                InnerStorage.UnsafeRelease(rawPointer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T UnsafeGetRef(long rawPointer)
            => ref InnerStorage.UnsafeGetRef(rawPointer);

        public void Clear()
        {
            foreach (var pointer in _pooled) {
                InnerStorage.UnsafeRelease(pointer);
            }
            _pooled.Clear();
        }

        public void Dispose()
        {
            InnerStorage.Dispose();
            _pooled = null!;
            GC.SuppressFinalize(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateAllocated(Action<long> func)
            => InnerStorage.IterateAllocated(func);
    }
}

}