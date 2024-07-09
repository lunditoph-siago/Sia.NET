#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class UnmanagedArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => false;
    public int Capacity => int.MaxValue;

    public int Count {
        get => _count;
        set {
            if (value == _count) {
                return;
            }
            if (value > _length) {
                _length = CalculateArraySize(value);
                var newMem = Marshal.AllocHGlobal(_length * Unsafe.SizeOf<T>());
                unsafe {
                    AsSpan().CopyTo(new Span<T>((void*)newMem, _length));
                }
                Marshal.FreeHGlobal(_mem);
                _mem = newMem;
            }
            _count = value;
        }
    }

    private int _count;
    private int _length = initialCapacity;
    private IntPtr _mem = Marshal.AllocHGlobal(initialCapacity * Unsafe.SizeOf<T>());

    public UnmanagedArrayBuffer() : this(0) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateArraySize(int requiredCapacity)
    {
        int size = 8;
        while (size < requiredCapacity) { size *= 2; }
        return size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T GetRef(int index)
        => ref new Span<T>((void*)_mem, _count)[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T GetRefOrNullRef(int index)
        => ref index < 0 || index >= Count ? ref Unsafe.NullRef<T>() : ref ((T*)_mem)[index];

    public unsafe Span<T> AsSpan() => new((void*)_mem, _count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}