namespace Sia.Tests.Entities;

public class BufferEntityHostTests
{
    [Fact]
    public void Release_RemovesEntityBeforeInvalidatingItsHandle()
    {
        var buffer = new ReleaseOrderBuffer<HList<int, EmptyHList>>();
        var host = BufferEntityHost<HList<int, EmptyHList>>.Create(buffer);
        var entity = host.Create(HList.From(1));
        buffer.Tracked = entity;

        host.Release(entity);

        Assert.True(buffer.WasValidWhileRemoving);
        Assert.False(entity.IsValid);
        Assert.Throws<ObjectDisposedException>(entity.Destroy);
    }

    [Fact]
    public void Release_ReusesStateWithoutRevivingTheStaleHandle()
    {
        var host = BufferEntityHost<HList<int, EmptyHList>>.Create(
            new ArrayBuffer<HList<int, EmptyHList>>());
        var stale = host.Create(HList.From(1));
        var staleId = stale.Id;
        var staleHashCode = stale.GetHashCode();

        host.Release(stale);
        var current = host.Create(HList.From(2));

        Assert.NotEqual(stale, current);
        Assert.NotEqual(staleId, current.Id);
        Assert.Equal(staleId, stale.Id);
        Assert.Equal(staleHashCode, stale.GetHashCode());
        Assert.False(stale.IsValid);
        Assert.Throws<ObjectDisposedException>(stale.Destroy);
        Assert.Throws<ObjectDisposedException>(() => stale.Get<int>());
        Assert.Throws<ObjectDisposedException>(() => stale.GetOrNullRef<int>());
        Assert.True(current.IsValid);
        Assert.Equal(2, current.Get<int>());
    }

    [Fact]
    public void Dispose_InvalidatesEveryEntityHandle()
    {
        var host = BufferEntityHost<HList<int, EmptyHList>>.Create(
            new ArrayBuffer<HList<int, EmptyHList>>());
        var first = host.Create(HList.From(1));
        var second = host.Create(HList.From(2));

        host.Dispose();

        Assert.False(first.IsValid);
        Assert.False(second.IsValid);
        Assert.Equal(0, host.Count);
    }

    [Fact]
    public void EntityIds_RemainUniqueAcrossPoolsAndDirectAllocation()
    {
        using var first = BufferEntityHost<HList<int, EmptyHList>>.Create(
            new ArrayBuffer<HList<int, EmptyHList>>());
        using var second = BufferEntityHost<HList<int, EmptyHList>>.Create(
            new ArrayBuffer<HList<int, EmptyHList>>());
        var ids = new HashSet<EntityId>();

        for (var i = 0; i < 1_024; i++) {
            var firstEntity = first.Create(HList.From(i));
            var secondEntity = second.Create(HList.From(i));

            Assert.True(ids.Add(firstEntity.Id));
            Assert.True(ids.Add(secondEntity.Id));
            Assert.True(ids.Add(EntityId.Create()));
            Assert.NotEqual(default, firstEntity.Id);
            Assert.NotEqual(default, secondEntity.Id);

            first.Release(firstEntity);
            second.Release(secondEntity);
        }
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
            if (value < _count && Tracked is { } tracked) {
                WasValidWhileRemoving = tracked.IsValid;
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
