using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public sealed class UnmanagedArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => false;
    public int Capacity => MaximumCapacity;

    private static int MaximumCapacity => (int)Math.Min(int.MaxValue, nint.MaxValue / Unsafe.SizeOf<T>());

    public int Count {
        get => _count;
        set {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > _allocatedLength) {
                Grow(value);
            }
            _count = value;
        }
    }

    private int _count;
    private int _allocatedLength = initialCapacity;
    private IntPtr _memory = Allocate(initialCapacity);
    private bool _disposed;

    public UnmanagedArrayBuffer() : this(0) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureCapacity(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count > _allocatedLength) Grow(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe void AppendReserved(in T value)
    {
        new Span<T>((void*)_memory, _allocatedLength)[_count] = value;
        _count++;
    }

    private static IntPtr Allocate(int initialCapacity)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            throw new NotSupportedException("Unmanaged storage requires elements without managed references.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialCapacity, MaximumCapacity);
        return initialCapacity == 0
            ? IntPtr.Zero
            : Marshal.AllocHGlobal(checked((nint)initialCapacity * Unsafe.SizeOf<T>()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int requiredCapacity)
    {
        var capacity = MaximumCapacity;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredCapacity, capacity);
        var size = Math.Min(8, capacity);
        while (size < requiredCapacity && size <= capacity / 2) {
            size *= 2;
        }

        var allocatedLength = Math.Max(size, requiredCapacity);
        var bytes = checked((nint)allocatedLength * Unsafe.SizeOf<T>());
        _memory = _memory == IntPtr.Zero
            ? Marshal.AllocHGlobal(bytes)
            : Marshal.ReAllocHGlobal(_memory, bytes);
        _allocatedLength = allocatedLength;
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
        if (_disposed) return;

        _disposed = true;
        Marshal.FreeHGlobal(_memory);
        _memory = IntPtr.Zero;
        _count = 0;
        _allocatedLength = 0;
    }
}
