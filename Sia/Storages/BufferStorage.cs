namespace Sia;

using System.Runtime.CompilerServices;

public static class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(in TBuffer buffer)
        where TBuffer : IBuffer<T>
        => new(buffer);
}

public class BufferStorage<T, TBuffer>(in TBuffer buffer) : StorageBase<T>
    where T : struct
    where TBuffer : IBuffer<T>
{
    public override int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

#pragma warning disable IDE0044 // Add readonly modifier
    private TBuffer _buffer = buffer;
#pragma warning restore IDE0044 // Add readonly modifier

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Allocate(int slot) => _buffer.CreateRef(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Release(int slot) => _buffer.Release(slot);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override ref T GetRef(int slot) => ref _buffer[slot];

    public override void Dispose()
    {
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}