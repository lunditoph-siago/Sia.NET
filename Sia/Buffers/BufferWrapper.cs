namespace Sia;

using System.Runtime.CompilerServices;

// see https://github.com/dotnet/runtime/issues/32815
public readonly struct BufferWrapper<T, TBuffer> : IBuffer<T>
    where TBuffer : IBuffer<T>
{
    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }
    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Count;
    }

    private readonly TBuffer _buffer;

    public BufferWrapper(TBuffer buffer)
    {
        _buffer = buffer;
    }

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
    public void Dispose()
        => _buffer.Dispose();
}