namespace Sia;

using System.Runtime.CompilerServices;

public sealed class BufferStorage<T>
    where T : struct
{
    public static BufferStorage<T, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<T>
        => new(buffer);
}

public class BufferStorage<T, TBuffer>(TBuffer buffer) : StorageBase<T>
    where T : struct
    where TBuffer : IBuffer<T>
{
    public override int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Capacity;
    }

    private readonly TBuffer _buffer = buffer;

    protected override void Allocate(int slot) => _buffer.CreateRef(slot);
    protected override void Release(int slot) => _buffer.Release(slot);
    protected override ref T GetRef(int slot) => ref _buffer.GetRef(slot);

    public override void Dispose()
    {
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}