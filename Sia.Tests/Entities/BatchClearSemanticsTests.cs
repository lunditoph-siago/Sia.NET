using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sia.Tests.Entities;

public sealed class BatchClearSemanticsTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }

    [Fact]
    public void Whole_host_moves_invalidate_enumerators_and_preserve_destination_order()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var preexisting = target.Create(HList.From(new B { Value = 8 }, new A { Value = 9 }));
        var entities = new Entity[3];
        for (var i = 0; i < entities.Length; i++) {
            entities[i] = source.Create(HList.From(new A { Value = i }));
        }
        using var sourceEnumerator = source.GetEnumerator();
        using var targetEnumerator = target.GetEnumerator();
        Assert.True(sourceEnumerator.MoveNext());
        Assert.True(targetEnumerator.MoveNext());
        source.AddComponentToAll(new B[] { new() { Value = 10 }, new() { Value = 11 }, new() { Value = 12 } });
        Assert.Throws<InvalidOperationException>(() => sourceEnumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => targetEnumerator.MoveNext());
        Assert.Equal(preexisting, target.GetEntity(0));
        for (var i = 0; i < entities.Length; i++) {
            Assert.Equal(entities[i], target.GetEntity(i + 1));
            Assert.Equal(i + 10, entities[i].Get<B>().Value);
        }
        using var reverseEnumerator = target.GetEnumerator();
        Assert.True(reverseEnumerator.MoveNext());
        target.RemoveHeadComponentFromAll();
        Assert.Throws<InvalidOperationException>(() => reverseEnumerator.MoveNext());
        Assert.Equal(4, source.Count);
        Assert.Equal(preexisting, source.GetEntity(0));
        for (var i = 0; i < entities.Length; i++) {
            Assert.Equal(entities[i], source.GetEntity(i + 1));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_source_host_does_not_retain_moved_state_after_world_disposal(bool remove)
    {
        var (state, retainedHost) = MoveAndDispose(remove);
        for (var i = 0; i < 6 && state.IsAlive; i++) {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Assert.False(state.IsAlive);
        GC.KeepAlive(retainedHost);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, IEntityHost) MoveAndDispose(bool remove)
    {
        var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var entity = remove
            ? target.Create(HList.From(new B { Value = 1 }, new A { Value = 2 }))
            : source.Create(HList.From(new A { Value = 2 }));
        var state = typeof(Entity).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entity)!;
        var weak = new WeakReference(state);
        if (remove) target.RemoveHeadComponentFromAll();
        else source.AddComponentToAll(new B[] { new() { Value = 1 } });
        world.Dispose();
        return (weak, remove ? target : source);
    }
}
