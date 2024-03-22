namespace Sia_Examples;

using Sia;

public static partial class Example6_Hierarchy
{
    public readonly record struct Name(string Value);

    public sealed class TestTag {}
    public static class TestNode
    {
        public static EntityRef Create(World world, string name, EntityRef? parent = null)
            => world.CreateInArrayHost(HList.Create(
                new Node<TestTag>(parent),
                 new Name(name)
            ));
    }

    public static void Run(World world)
    {
        world.AcquireAddon<Hierarchy<TestTag>>();

        var e1 = TestNode.Create(world, "test1");
        var e2 = TestNode.Create(world, "test2", e1);
        var e3 = TestNode.Create(world, "test3", e1);
        var e4 = TestNode.Create(world, "test4", e3);

        foreach (var child in e1.Get<Node<TestTag>>().Children) {
            Console.WriteLine(child.Get<Name>().Value);
        }

        Console.WriteLine("===");
        world.Modify(e4, new Node<TestTag>.SetParent(e1));

        foreach (var child in e1.Get<Node<TestTag>>().Children) {
            Console.WriteLine(child.Get<Name>().Value);
        }

        e4.Dispose();
        Console.WriteLine(world.Count);

        e1.Dispose();
        Console.WriteLine(world.Count);
    }
}