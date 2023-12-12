#pragma warning disable CS8500

namespace Sia;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections;

public sealed class UnmanagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static UnmanagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count => _allocated.Count;
    public bool IsManaged => false;

    private HashSet<nint> _allocated = [];

    private static readonly int ElementSize = Unsafe.SizeOf<T>();

    private UnmanagedHeapStorage() {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint UnsafeAllocate(out int version)
        => UnsafeAllocate(default, out version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe nint UnsafeAllocate(in T initial, out int version)
    {
        var ptr = Marshal.AllocHGlobal(ElementSize);
        _allocated.Add(ptr);
        *(T*)ptr = initial;
        version = 1;
        return ptr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(nint rawPointer, int version)
    {
        if (version != 1 || !_allocated.Remove(rawPointer)) {
            throw new ArgumentException("Invalid pointer");
        }
        Marshal.FreeHGlobal(rawPointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T UnsafeGetRef(nint rawPointer, int version)
    {
        if (version != 1) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref *(T*)rawPointer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        foreach (var pointer in _allocated) {
            handler(pointer, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        foreach (var pointer in _allocated) {
            handler(data, pointer, 1);
        }
    }

    public IEnumerator<(nint, int)> GetEnumerator()
    {
        foreach (var pointer in _allocated) {
            yield return (pointer, 1);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
    
    public void Dispose()
    {
        foreach (var ptr in _allocated) {
            Marshal.FreeHGlobal((nint)ptr);
        }
        _allocated = null!;
    }
}