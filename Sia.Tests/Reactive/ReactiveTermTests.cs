
using global::Sia.Reactive;

using BranchList = Sia.HList<Sia.Tests.Reactive.BranchValue, Sia.EmptyHList>;
using ItemList = Sia.HList<Sia.Tests.Reactive.KeyedValue, Sia.EmptyHList>;
using ScopedList = Sia.HList<Sia.Tests.Reactive.ScopedValue, Sia.EmptyHList>;

namespace Sia.Tests.Reactive;

public class ReactiveTermTests
{
    [Fact]
    public void Cond_SwitchesOwnershipWithoutLeakingTheInactiveBranch()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var probe = new BranchProbe();
        var mount = reconciler.Mount(new BranchSpec(probe));

        Assert.Single(FindAll<BranchValue>(world));
        probe.Visible.Set(false);
        reconciler.Flush();

        Assert.Empty(FindAll<BranchValue>(world));

        probe.Visible.Set(true);
        reconciler.Flush();
        Assert.Single(FindAll<BranchValue>(world));

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ForEach_PreservesKeyIdentityAcrossReorderAndRejectsDuplicates()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var mount = reconciler.Mount(new ListSpec(new KeyedValue[] {
            new(1, 10),
            new(2, 20),
        }));
        var initial = FindAll<KeyedValue>(world)
            .ToDictionary(entity => entity.Get<KeyedValue>().Key);

        mount.Update(new ListSpec(new KeyedValue[] {
            new(2, 21),
            new(1, 11),
        }));
        reconciler.Flush();
        var reordered = FindAll<KeyedValue>(world)
            .ToDictionary(entity => entity.Get<KeyedValue>().Key);

        Assert.Equal(initial[1], reordered[1]);
        Assert.Equal(initial[2], reordered[2]);
        Assert.Equal(21, reordered[2].Get<KeyedValue>().Value);

        mount.Update(new ListSpec(new KeyedValue[] { new(2, 22) }));
        reconciler.Flush();
        var remaining = FindAll<KeyedValue>(world);
        Assert.DoesNotContain(remaining, entity => entity.Get<KeyedValue>().Key == 1);
        Assert.Equal(initial[2], Assert.Single(remaining));

        mount.Update(new ListSpec(new KeyedValue[] { new(2, 1), new(2, 2) }));
        Assert.Throws<InvalidOperationException>(reconciler.Flush);
    }

    [Fact]
    public void ForEach_RecycledStorageDoesNotRetainPreviousItems()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();

        for (var i = 0; i < 4; i++) {
            var mount = reconciler.Mount(new ListSpec(new KeyedValue[] {
                new KeyedValue(i, i * 10),
            }));

            var output = Assert.Single(FindAll<KeyedValue>(world));
            Assert.Equal(i, output.Get<KeyedValue>().Key);

            mount.Unmount();
            Assert.Empty(FindAll<KeyedValue>(world));
        }
    }

    [Fact]
    public void PatchForEach_UpdatesOnlyNamedKeysAndRemovesExplicitKeys()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var mount = reconciler.Mount(new PatchListSpec(
            new KeyedValue[] { new(1, 10), new(2, 20) },
            Array.Empty<int>()));
        var initial = FindAll<KeyedValue>(world)
            .ToDictionary(entity => entity.Get<KeyedValue>().Key);

        mount.Update(new PatchListSpec(
            new KeyedValue[] { new(2, 21), new(3, 30) },
            Array.Empty<int>()));
        reconciler.Flush();
        var patched = FindAll<KeyedValue>(world)
            .ToDictionary(entity => entity.Get<KeyedValue>().Key);

        Assert.Equal(initial[1], patched[1]);
        Assert.Equal(initial[2], patched[2]);
        Assert.Equal(21, patched[2].Get<KeyedValue>().Value);
        Assert.Equal(30, patched[3].Get<KeyedValue>().Value);

        mount.Update(new PatchListSpec(Array.Empty<KeyedValue>(), new[] { 1 }));
        reconciler.Flush();
        var remaining = FindAll<KeyedValue>(world)
            .ToDictionary(entity => entity.Get<KeyedValue>().Key);
        Assert.DoesNotContain(1, remaining.Keys);
        Assert.Equal(patched[2], remaining[2]);
        Assert.Equal(patched[3], remaining[3]);
    }

    [Fact]
    public void UseRef_PreservesOneValueAcrossComponentUpdates()
    {
        using var world = new World();
        var probe = new RefProbe();
        var mount = world.Mount(RefComponent.Definition, new(probe, 1));
        world.FlushReactive();

        mount.Update(new(probe, 2));
        world.FlushReactive();

        Assert.Equal(2, probe.Values.Count);
        Assert.Same(probe.Values[0], probe.Values[1]);
    }

    [Fact]
    public void Scope_InvalidatesConsumersAndUnsubscribesThemOnUnmount()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var probe = new ScopeProbe();
        var mount = reconciler.Mount(new ScopeSpec(probe));
        var output = FindAll<ScopedValue>(world).Single();

        probe.Theme.Set(new Theme(2));
        reconciler.Flush();

        Assert.Equal(output, FindAll<ScopedValue>(world).Single());
        Assert.Equal(2, output.Get<ScopedValue>().Theme);
        Assert.Equal(2, probe.ConsumerExpansions);

        probe.Visible.Set(false);
        reconciler.Flush();
        Assert.Empty(FindAll<ScopedValue>(world));

        probe.Theme.Set(new Theme(3));
        reconciler.Flush();
        Assert.Equal(2, probe.ConsumerExpansions);
        Assert.Empty(FindAll<ScopedValue>(world));

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void Effect_FollowsMountReconcileAndUnmount()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<string>();
        var mount = reconciler.Mount(new EffectSpec(calls, 1));

        mount.Update(new EffectSpec(calls, 2));
        reconciler.Flush();
        mount.Unmount();

        Assert.Equal(["mount 1", "reconcile 1 -> 2", "unmount 2"], calls);
        Assert.Equal(0, world.Count);
    }

    private static Entity[] FindAll<T>(World world)
    {
        using var query = world.Query(Matchers.Of<T>());
        return [.. query.Hosts.SelectMany(static host => host)];
    }
}

public readonly record struct BranchValue(int Value);
public readonly record struct KeyedValue(int Key, int Value);
public readonly record struct Theme(int Value);
public readonly record struct ScopedValue(int Theme);

public readonly record struct EffectSpec(List<string> Calls, int Value)
    : ISpec<EffectSpec, Unit, EffectTerm<ProbeEffect>>
{
    public static EffectTerm<ProbeEffect> Expand(
        in EffectSpec props,
        in Unit state,
        in ExpandContext context)
        => Term.Effect(new ProbeEffect(props.Calls, props.Value));
}

public readonly record struct ProbeEffect(List<string> Calls, int Value)
    : IEffect<ProbeEffect>
{
    public static void Mount(in ProbeEffect self)
        => self.Calls.Add($"mount {self.Value}");

    public static void Reconcile(in ProbeEffect previous, in ProbeEffect next)
        => next.Calls.Add($"reconcile {previous.Value} -> {next.Value}");

    public static void Unmount(in ProbeEffect self)
        => self.Calls.Add($"unmount {self.Value}");
}

public sealed class BranchProbe
{
    public StateRef<bool> Visible;
}

public readonly record struct BranchSpec(BranchProbe Probe)
    : ISpec<BranchSpec, bool, CondTerm<EntityTerm<BranchList, UnitTerm>>>
{
    public static bool InitialState(in BranchSpec props) => true;

    public static CondTerm<EntityTerm<BranchList, UnitTerm>> Expand(
        in BranchSpec props,
        in bool state,
        in ExpandContext context)
    {
        props.Probe.Visible = context.UseState<bool>();
        return Term.Cond(state, Term.Entity(HList.From(new BranchValue(1))));
    }
}

public readonly record struct ListSpec(ReadOnlyMemory<KeyedValue> Items)
    : ISpec<ListSpec, int, ForEachTerm<int, ItemSpec>>
{
    public static ForEachTerm<int, ItemSpec> Expand(
        in ListSpec props,
        in int state,
        in ExpandContext context)
        => Term.ForEach<int, ItemSpec>(
            props.Items.ToArray()
                .Select(static item => Term.Keyed(item.Key, new ItemSpec(item)))
                .ToArray());
}

public readonly record struct PatchListSpec(
    ReadOnlyMemory<KeyedValue> Upserts,
    ReadOnlyMemory<int> Removals)
    : ISpec<PatchListSpec, int, PatchForEachTerm<int, ItemSpec>>
{
    public static PatchForEachTerm<int, ItemSpec> Expand(
        in PatchListSpec props,
        in int state,
        in ExpandContext context)
        => new(
            props.Upserts.ToArray()
                .Select(static item => Term.Keyed(item.Key, new ItemSpec(item)))
                .ToArray(),
            props.Removals);
}

public readonly record struct ItemSpec(KeyedValue Item)
    : ISpec<ItemSpec, int, EntityTerm<ItemList, UnitTerm>>
{
    public static EntityTerm<ItemList, UnitTerm> Expand(
        in ItemSpec props,
        in int state,
        in ExpandContext context)
        => Term.Entity(HList.From(props.Item));
}

public sealed class RefProbe
{
    public List<object> Values { get; } = [];
}

public readonly record struct RefProps(RefProbe Probe, int Version);

[ReactiveComponent]
public static partial class RefComponent
{
    public static ReactiveNode Render(in RefProps props, ref Hooks hooks)
    {
        props.Probe.Values.Add(hooks.UseRef(static () => new object()).Value);
        return global::Sia.Reactive.Reactive.None;
    }
}

public sealed class ScopeProbe
{
    public StateRef<Theme> Theme;
    public State<bool> Visible;
    public int ConsumerExpansions;
}

public readonly record struct ScopeSpec(ScopeProbe Probe)
    : ISpec<ScopeSpec, Theme,
        ScopeTerm<Theme, CondTerm<LiftTerm<ScopeConsumerSpec>>>>
{
    public static Theme InitialState(in ScopeSpec props) => new(1);

    public static ScopeTerm<Theme, CondTerm<LiftTerm<ScopeConsumerSpec>>> Expand(
        in ScopeSpec props,
        in Theme state,
        in ExpandContext context)
    {
        props.Probe.Theme = context.UseState<Theme>();
        props.Probe.Visible = context.UseState(true);
        return Term.Scope(
            state,
            Term.Cond(
                props.Probe.Visible.Value,
                Term.Lift(new ScopeConsumerSpec(props.Probe))));
    }
}

public readonly record struct ScopeConsumerSpec(ScopeProbe Probe)
    : ISpec<ScopeConsumerSpec, int, EntityTerm<ScopedList, UnitTerm>>
{
    public static EntityTerm<ScopedList, UnitTerm> Expand(
        in ScopeConsumerSpec props,
        in int state,
        in ExpandContext context)
    {
        props.Probe.ConsumerExpansions++;
        return Term.Entity(HList.From(
            new ScopedValue(context.Use<Theme>().Value)));
    }
}
