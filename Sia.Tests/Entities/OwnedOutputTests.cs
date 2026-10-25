using System.Runtime.CompilerServices;

namespace Sia.Tests.Entities;

public sealed class OwnedOutputTests
{
    public readonly record struct Value(long Number, object? Reference);

    [Fact]
    public void Create_ObserverFailure_ExposesAcquiredIdentityForRollback()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        Entity acquired = default;
        host.OnEntityCreated += entity => {
            Assert.Equal(acquired, entity);
            throw new InvalidOperationException("creation observer");
        };

        Assert.Throws<InvalidOperationException>(() =>
            host.Create(HList.From(new Value(42, null)), out acquired));

        Assert.True(acquired.IsValid);
        Assert.Equal(42, acquired.Get<Value>().Number);
        Assert.Equal(1, host.Count);
        Assert.Equal(1, world.Count);

        host.ReleaseOwned(acquired);
        Assert.False(acquired.IsValid);
        Assert.Equal(0, host.Count);
        Assert.Equal(0, world.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseCallback_CannotReleaseTheSameEntityTwice(bool useOwnedRelease)
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        var entity = host.Create(HList.From(new Value(1, null)));
        var calls = 0;
        host.OnEntityReleased += e => { calls++; e.Destroy(); };
        if (useOwnedRelease)
            host.ReleaseOwned(entity);
        else
            entity.Destroy();
        Assert.Equal(1, calls);
        Assert.Equal(0, world.Count);
        Assert.Equal(0, host.Count);
        Assert.False(entity.IsValid);
        var reused = host.Create(HList.From(new Value(2, null)));
        reused.Destroy();
        Assert.Equal(2, calls);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void OwnedRelease_ReportsFailuresAndReleasesStorage()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        var entity = host.Create();
        var componentError = new InvalidOperationException("component remove");
        var entityError = new InvalidOperationException("entity remove");
        var hostError = new InvalidOperationException("host release");
        world.Dispatcher.Listen<WorldEvents.Remove<Value>>((Entity _, in WorldEvents.Remove<Value> e) => throw componentError);
        world.Dispatcher.Listen<WorldEvents.Remove>((Entity _, in WorldEvents.Remove e) => throw entityError);
        host.OnEntityReleased += _ => throw hostError;
        var failure = Assert.Throws<AggregateException>(() => host.ReleaseOwned(entity));
        Assert.Equal(new Exception[] { componentError, entityError, hostError }, failure.InnerExceptions);
        Assert.Equal(0, host.Count);
        Assert.Equal(0, world.Count);
        Assert.False(entity.IsValid);
        Assert.False(world.Dispatcher.IsSending);
    }

    [Fact]
    public void OwnedStorage_RejectsStaleAndWrongHostEntities()
    {
        using var world = new World();
        using var other = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        var old = host.Create(HList.From(new Value(1, null)));
        old.Destroy();
        var current = host.Create(HList.From(new Value(2, null)));
        var foreign = other.Create(HList.From(new Value(3, null)));
        Assert.Throws<ObjectDisposedException>(() => Write(host, old, new Value(9, null)));
        Assert.Throws<InvalidOperationException>(() => Write(host, foreign, new Value(9, null)));
        Assert.Equal(2, current.Get<Value>().Number);
        Assert.Equal(3, foreign.Get<Value>().Number);
    }

    [Fact]
    public void OwnedWrite_ReacquiresStorageAfterGrowthAndSwapRemoval()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        var first = host.Create(HList.From(new Value(-1, null)));
        var target = host.Create(HList.From(new Value(0, null)));
        var calls = 0;
        world.Dispatcher.Listen<WorldEvents.Set<Value>>((Entity e, in WorldEvents.Set<Value> _) => {
            if (e != target) {
                return false;
            }
            if (++calls == 1) {


                world.Dispatcher.RunAfterSend(() => {
                    first.Destroy();
                    for (var i = 0; i < 256; i++)
                        host.Create(HList.From(new Value(i, null)));
                });
            }
            return false;
        });
        Write(host, target, new Value(42, null));
        Write(host, target, new Value(43, null));
        Assert.Equal(43, target.Get<Value>().Number);
        Assert.Equal(new Value(43, null), target.Get<Value>());
        Assert.Equal(2, calls);
        Assert.Equal(257, host.Count);
    }

    [Fact]
    public void OwnedWrite_PreservesOtherComponents()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<long, HList<Value, EmptyHList>>>();
        var entity = host.Create(HList.From(123L, new Value(1, null)));
        var reference = new object();
        host.GetOwnedRef(entity).Tail.Head = new Value(42, reference);
        host.NotifyOwnedSet<Value>(entity);
        Assert.Equal(123L, entity.Get<long>());
        Assert.Equal(42, entity.Get<Value>().Number);
        Assert.Same(reference, entity.Get<Value>().Reference);
        Assert.Equal(new Value(42, reference), entity.Get<Value>());
    }

    [Fact]
    public void OwnedWrite_PreservesManagedReferencesAcrossCollection()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<Value, EmptyHList>>();
        var entity = host.Create();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        var weak = StoreYoungReference(host, entity);
        GC.Collect(0, GCCollectionMode.Forced, true, true);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Assert.True(weak.IsAlive);
        Assert.Same(weak.Target, entity.Get<Value>().Reference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StoreYoungReference(WorldEntityHost<HList<Value, EmptyHList>, ArrayEntityHost<HList<Value, EmptyHList>>> host, Entity entity)
    {
        var reference = new object();
        Write(host, entity, new Value(42, reference));
        return new(reference);
    }

    private static void Write(WorldEntityHost<HList<Value, EmptyHList>, ArrayEntityHost<HList<Value, EmptyHList>>> host, Entity entity, in Value value)
    {
        host.GetOwnedRef(entity).Head = value;
        host.NotifyOwnedSet<Value>(entity);
    }
}
