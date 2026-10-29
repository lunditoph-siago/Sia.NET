using System.Runtime.CompilerServices;

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
        Assert.Throws<ObjectDisposedException>(() => stale.GetOrNullRef<LaterMissing>());
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
    public void BulkIteration_AddressesSequentialLayoutAcrossArchetypes()
    {
        using var world = new World();
        for (var i = 0; i < 96; i++) {
            if ((i & 1) == 0) {
                world.Create(HList.From(i, 1L));
            }
            else {
                world.Create(HList.From(i, 1f));
            }
        }
        var query = world.Query(Matchers.Of<int>());

        var sum = 0;
        query.ForSlice((ref int value) => sum += value);
        Assert.Equal(4560, sum);

        query.ForSlice((ref int value) => value += 1);
        var total = 0;
        foreach (var entity in query) {
            total += entity.Get<int>();
        }
        Assert.Equal(4560 + 96, total);

        var host = world.GetArrayHost<HList<int, HList<long, EmptyHList>>>();
        var hostSum = 0L;
        host.ForSlice((ref long value) => hostSum += value);
        Assert.Equal(48, hostSum);
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

    public struct Present { public int Value; public string? Name; }
    public struct EarlierMissing { public int Value; }
    public struct LaterMissing { public int Value; }

    public sealed class ExceptionalHost<T> : ArrayEntityHost<T>, IEntityHost<T> where T : struct, IHList
    {
        public bool ThrowDescriptor;
        public bool ThrowBytes;
        EntityDescriptor IEntityHost.Descriptor => ThrowDescriptor ? throw new InvalidOperationException("descriptor") : Descriptor;
        ref byte IEntityHost.GetByteRef(int slot)
        {
            if (ThrowBytes) throw new InvalidOperationException("bytes");
            return ref GetByteRef(slot);
        }
    }

    [Fact]
    public void Present_returns_live_reference_and_missing_returns_null_reference()
    {
        _ = EntityDescriptor.Get<HList<EarlierMissing, EmptyHList>>();
        using var world = new World();
        var e = world.Create(HList.From(new Present { Value = 1, Name = "kept" }));
        ref var present = ref e.GetOrNullRef<Present>();
        Assert.False(Unsafe.IsNullRef(ref present));
        present.Value = 8;
        present.Name = "changed";
        Assert.Equal(8, e.Get<Present>().Value);
        Assert.Equal("changed", e.Get<Present>().Name);
        Assert.True(Unsafe.IsNullRef(ref e.GetOrNullRef<EarlierMissing>()));
        Assert.True(Unsafe.IsNullRef(ref e.GetOrNullRef<LaterMissing>()));
        Assert.Throws<ComponentNotFoundException>(() => e.Get<LaterMissing>());
    }

    [Fact]
    public void Descriptor_error_is_caught_but_byte_access_error_propagates()
    {
        using var host = new ExceptionalHost<HList<Present, EmptyHList>>();
        var e = host.Create(HList.From(new Present { Value = 5 }));
        host.ThrowDescriptor = true;
        Assert.True(Unsafe.IsNullRef(ref e.GetOrNullRef<Present>()));
        host.ThrowBytes = true;
        var error = Assert.Throws<InvalidOperationException>(() => e.GetOrNullRef<Present>());
        Assert.Equal("bytes", error.Message);
        host.ThrowBytes = false;
        host.ThrowDescriptor = false;
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
