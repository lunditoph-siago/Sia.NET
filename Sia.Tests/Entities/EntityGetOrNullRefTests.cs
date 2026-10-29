namespace Sia.Tests.Entities;

using System.Runtime.CompilerServices;

public sealed class EntityGetOrNullRefTests
{
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
    public void Stale_entity_throws_before_component_lookup()
    {
        using var world = new World();
        var e = world.Create(HList.From(new Present()));
        e.Destroy();
        Assert.Throws<ObjectDisposedException>(() => e.GetOrNullRef<Present>());
        Assert.Throws<ObjectDisposedException>(() => e.GetOrNullRef<LaterMissing>());
    }

    [Fact]
    public void Descriptor_error_is_caught_but_byte_access_error_propagates()
    {
        using var world = new World();
        var inner = new ExceptionalHost<HList<Present, EmptyHList>>();
        var host = new WorldEntityHost<HList<Present, EmptyHList>, ExceptionalHost<HList<Present, EmptyHList>>>(world, inner);
        world.UnsafeAddRawHost(host);
        var e = host.Create(HList.From(new Present { Value = 5 }));
        inner.ThrowDescriptor = true;
        Assert.True(Unsafe.IsNullRef(ref e.GetOrNullRef<Present>()));
        inner.ThrowBytes = true;
        var error = Assert.Throws<InvalidOperationException>(() => e.GetOrNullRef<Present>());
        Assert.Equal("bytes", error.Message);
        inner.ThrowBytes = false;
        inner.ThrowDescriptor = false;
    }
}
