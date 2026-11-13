using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public sealed class UnmanagedArrayBuffer<T> : IBuffer<T>
{
    public bool IsManaged => false;
    public int Capacity => (int)Math.Min(int.MaxValue, nint.MaxValue / Unsafe.SizeOf<T>());

    public int Count {
        get => _count;
        set {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > _allocatedLength) {
                var allocatedLength = CalculateArraySize(value);
                var bytes = checked((nint)allocatedLength * Unsafe.SizeOf<T>());
                _memory = _memory == IntPtr.Zero
                    ? Marshal.AllocHGlobal(bytes)
                    : Marshal.ReAllocHGlobal(_memory, bytes);
                _allocatedLength = allocatedLength;
            }
            _count = value;
        }
    }

    private int _count;
    private int _allocatedLength;
    private IntPtr _memory;
    private bool _disposed;

    public UnmanagedArrayBuffer(int initialCapacity)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            throw new NotSupportedException("Unmanaged storage requires elements without managed references.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialCapacity, Capacity);
        if (initialCapacity != 0) {
            _memory = Marshal.AllocHGlobal(checked((nint)initialCapacity * Unsafe.SizeOf<T>()));
        }
        _allocatedLength = initialCapacity;
    }

    public UnmanagedArrayBuffer() : this(0)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateArraySize(int requiredCapacity)
    {
        var capacity = Capacity;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredCapacity, capacity);
        var size = Math.Min(8, capacity);
        while (size < requiredCapacity && size <= capacity / 2) { size *= 2; }
        return Math.Max(size, requiredCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref AsSpan()[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
        => ref (uint)index >= (uint)_count ? ref Unsafe.NullRef<T>() : ref AsSpan()[index];

    public unsafe Span<T> AsSpan() => new((void*)_memory, Count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;
        Marshal.FreeHGlobal(_memory);
        _memory = IntPtr.Zero;
        _count = 0;
        _allocatedLength = 0;
    }
}
