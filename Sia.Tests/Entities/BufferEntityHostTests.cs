namespace Sia.Tests.Entities;

public class BufferEntityHostTests
{
    [Fact]
    public void Release_RemovesEntityBeforeReturningItToThePool()
    {
        var buffer = new ReleaseOrderBuffer<HList<int, EmptyHList>>();
        var host = BufferEntityHost<HList<int, EmptyHList>>.Create(buffer);
        var entity = host.Create(HList.From(1));
        buffer.Tracked = entity;

        host.Release(entity);

        Assert.True(buffer.WasValidWhileRemoving);
    }
}

internal sealed class ReleaseOrderBuffer<T> : IBuffer<T>
{
    private readonly T[] _items = new T[4];
    private int _count;

    public Entity? Tracked;
    public bool WasValidWhileRemoving;
    public bool IsManaged => true;
    public int Capacity => _items.Length;

    public int Count {
        get => _count;
        set {
            if (value < _count && Tracked != null) {
                WasValidWhileRemoving = Tracked.IsValid;
            }
            _count = value;
        }
    }

    public ref T GetRef(int index) => ref _items[index];

    public ref T GetRefOrNullRef(int index)
        => ref (uint)index < (uint)_items.Length
            ? ref _items[index]
            : ref System.Runtime.CompilerServices.Unsafe.NullRef<T>();

    public Span<T> AsSpan() => _items;
    public void Dispose() { }
}
