namespace Sia_Examples;

using System.Numerics;
using Sia;

public static partial class Example12_EntityBuilder
{
    public record struct C1(int Value);
    public record struct C2(string Value);
    public record struct C3(Vector3 Value);

    public static void Run(World world)
    {
        var builder = new EntityBuilder();

        builder.Add(new C1(114514));
        Console.WriteLine(builder.Boxed);

        builder.Add(new C2("Hello world"));
        Console.WriteLine(builder.Boxed);

        builder.Add(new C3(Vector3.One));
        Console.WriteLine(builder.Boxed);

        var entity = builder.Create(new WorldEntityCreators.Bucket(world));
        Console.WriteLine(entity.Boxed);

        Console.WriteLine(entity.Get<C1>());
        Console.WriteLine(entity.Get<C2>());
        Console.WriteLine(entity.Get<C3>());
    }
}