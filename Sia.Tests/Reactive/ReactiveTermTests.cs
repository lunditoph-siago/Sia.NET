namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using BranchList = HList<BranchValue, EmptyHList>;
using ItemList = HList<KeyedValue, EmptyHList>;

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

        Assert.Same(initial[1], reordered[1]);
        Assert.Same(initial[2], reordered[2]);
        Assert.Equal(21, reordered[2].Get<KeyedValue>().Value);

        mount.Update(new ListSpec(new KeyedValue[] { new(2, 22) }));
        reconciler.Flush();
        Assert.False(initial[1].IsValid);
        Assert.Same(initial[2], FindAll<KeyedValue>(world).Single());

        mount.Update(new ListSpec(new KeyedValue[] { new(2, 1), new(2, 2) }));
        Assert.Throws<InvalidOperationException>(reconciler.Flush);
    }

    private static Entity[] FindAll<T>(World world)
    {
        using var query = world.Query(Matchers.Of<T>());
        return [.. query.Hosts.SelectMany(static host => host)];
    }
}

public readonly record struct BranchValue(int Value);
public readonly record struct KeyedValue(int Key, int Value);

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
