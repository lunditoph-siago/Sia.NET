namespace Sia.Tests.Reactors;

using Sia.Reactors;

public class HierarchyTests
{
    public sealed class TestTag;

    private readonly record struct Name(string Value);

    private static Entity CreateNode(World world, string name, Entity? parent = null)
        => world.Create(HList.From(
            parent is { } attached ? new Node<TestTag>(attached) : new Node<TestTag>(),
            new Name(name)));

    [Fact]
    public void CreatingChildrenWithAParent_PopulatesTheParentsChildrenInCreationOrder()
    {
        using var world = new World();
        world.AcquireAddon<Hierarchy<TestTag>>();

        var root = CreateNode(world, "root");
        var first = CreateNode(world, "first", root);
        var second = CreateNode(world, "second", root);

        var childNames = root.Get<Node<TestTag>>().Children
            .Select(child => child.Get<Name>().Value);

        Assert.Equal(["first", "second"], childNames);
    }

    [Fact]
    public void ExecutingSetParent_ReparentsTheEntityUnderTheNewParent()
    {
        using var world = new World();
        world.AcquireAddon<Hierarchy<TestTag>>();

        var parent = CreateNode(world, "parent");
        var orphan = CreateNode(world, "orphan");

        world.Execute(orphan, new Node<TestTag>.SetParent(parent));

        Assert.Contains(orphan, parent.Get<Node<TestTag>>().Children);
    }

    [Fact]
    public void DestroyingAParentWithAChild_DoesNotAliasChildrenAcrossNewParents()
    {
        using var world = new World();
        world.AcquireAddon<Hierarchy<TestTag>>();

        var a = world.Create(HList.From(new Node<TestTag>()));
        var c1 = world.Create(HList.From(new Node<TestTag>(a)));

        a.Destroy();

        var b = world.Create(HList.From(new Node<TestTag>()));
        var d = world.Create(HList.From(new Node<TestTag>()));

        var c2 = world.Create(HList.From(new Node<TestTag>(b)));
        var c3 = world.Create(HList.From(new Node<TestTag>(d)));

        var bChildren = b.Get<Node<TestTag>>().Children;
        var dChildren = d.Get<Node<TestTag>>().Children;

        Assert.Single(bChildren);
        Assert.Single(dChildren);
        Assert.Contains(c2, bChildren);
        Assert.Contains(c3, dChildren);
        Assert.False(c1.IsValid);
    }
}
