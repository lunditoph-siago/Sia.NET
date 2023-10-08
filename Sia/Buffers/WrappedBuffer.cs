namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// see https://github.com/dotnet/runtime/issues/32815
public readonly struct WrappedBuffer<T, TBuffer> : IBuffer<T>
    where TBuffer : IBuffer<T>
{
    public TBuffer InnerBuffer => _buffer;

    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Count;
    }

    private readonly TBuffer _buffer;

    public WrappedBuffer(TBuffer buffer)
    {
        _buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int index)
        => _buffer.Contains(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
        => ref _buffer.GetOrAddValueRef(index, out exists);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
        => ref _buffer.GetValueRefOrNullRef(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index)
        => _buffer.Remove(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
        => _buffer.Remove(index, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(BufferIndexHandler handler)
        => _buffer.IterateAllocated(handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, BufferIndexHandler<TData> handler)
        => _buffer.IterateAllocated(data, handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
        => _buffer.Dispose();
}