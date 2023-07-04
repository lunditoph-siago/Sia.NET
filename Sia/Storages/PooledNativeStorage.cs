namespace Sia;

using System.Runtime.InteropServices;

public class PooledNativeStorage<T> : IStorage<T>
{
    public int PoolSize { get; set; }
    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }

    private Stack<IntPtr> _pooled = new();

    private static readonly int MemorySize = Marshal.SizeOf<T>();

    public PooledNativeStorage(int poolSize)
    {
        PoolSize = poolSize;
    }

    public virtual IntPtr Allocate()
    {
        if (!_pooled.TryPop(out var ptr)) {
            ptr = Marshal.AllocHGlobal(MemorySize);
        }
        return ptr;
    }

    public virtual void Release(IntPtr ptr)
    {
        if (_pooled.Count < PoolSize) {
            _pooled.Push(ptr);
        }
        else {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void Clear()
    {
        _pooled.Clear();
    }
}