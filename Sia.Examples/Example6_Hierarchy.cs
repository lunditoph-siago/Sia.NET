namespace Sia_Examples;

using Sia;
using Sia.Reactors;

public static partial class Example6_Hierarchy
{
    public readonly record struct Name(string Value);

    public sealed class TestTag {}
    public static class TestNode
    {
        public static Entity Create(World world, string name, Entity? parent = null)
            => world.Create(HList.From(
                new Node<TestTag>(parent),
                new Name(name)
            ));
    }

    public static void Run(World world)
    {
        var e1 = TestNode.Create(world, "test1");
        var e2 = TestNode.Create(world, "test2", e1);
        var e3 = TestNode.Create(world, "test3", e1);
        var e4 = TestNode.Create(world, "test4", e3);

        foreach (var child in e1.Get<Node<TestTag>>().Children) {
            Console.WriteLine(child.Get<Name>().Value);
        }

        Console.WriteLine("===");
        world.Execute(e4, new Node<TestTag>.SetParent(e1));

        foreach (var child in e1.Get<Node<TestTag>>().Children) {
            Console.WriteLine(child.Get<Name>().Value);
        }

        Console.WriteLine("Before SetIsSelfEnabled:");
        Console.WriteLine(e2.Get<Node<TestTag>>().IsEnabled);
        Console.WriteLine(e3.Get<Node<TestTag>>().IsEnabled);
        Console.WriteLine(e4.Get<Node<TestTag>>().IsEnabled);

        _ = new Node<TestTag>.View(e1) {
            IsSelfEnabled = false
        };

        Console.WriteLine("After SetIsSelfEnabled:");
        Console.WriteLine(e2.Get<Node<TestTag>>().IsEnabled);
        Console.WriteLine(e3.Get<Node<TestTag>>().IsEnabled);
        Console.WriteLine(e4.Get<Node<TestTag>>().IsEnabled);

        e4.Dispose();
        Console.WriteLine(world.Count);

        e1.Dispose();
        Console.WriteLine(world.Count);
    }
}