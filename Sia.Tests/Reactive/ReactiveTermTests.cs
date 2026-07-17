namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using BranchList = HList<BranchValue, EmptyHList>;
using ItemList = HList<KeyedValue, EmptyHList>;
using ScopedList = HList<ScopedValue, EmptyHList>;

public class ReactiveTermTests
{
    [Fact]
    public void Cond_SwitchesOwnershipWithoutLeakingTheInactiveBranch()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var probe = new BranchProbe();
        var mount = reconciler.Mount(new BranchSpec(probe));

        var first = FindAll<BranchValue>(world).Single();
        probe.Visible.Set(false);
        reconciler.Flush();

        Assert.False(first.IsValid);
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

        mount.Unmount();
        Assert.False(output.IsValid);
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

public readonly record struct ItemSpec(KeyedValue Item)
    : ISpec<ItemSpec, int, EntityTerm<ItemList, UnitTerm>>
{
    public static EntityTerm<ItemList, UnitTerm> Expand(
        in ItemSpec props,
        in int state,
        in ExpandContext context)
        => Term.Entity(HList.From(props.Item));
}

public sealed class ScopeProbe
{
    public StateRef<Theme> Theme;
    public int ConsumerExpansions;
}

public readonly record struct ScopeSpec(ScopeProbe Probe)
    : ISpec<ScopeSpec, Theme,
        ScopeTerm<Theme, LiftTerm<ScopeConsumerSpec>>>
{
    public static Theme InitialState(in ScopeSpec props) => new(1);

    public static ScopeTerm<Theme, LiftTerm<ScopeConsumerSpec>> Expand(
        in ScopeSpec props,
        in Theme state,
        in ExpandContext context)
    {
        props.Probe.Theme = context.UseState<Theme>();
        return Term.Scope(state, Term.Lift(new ScopeConsumerSpec(props.Probe)));
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
