namespace Sia_Examples;

using System.Numerics;
using Sia;

public static partial class Example12_DynEntityRef
{
    public record struct C1(int Value);
    public record struct C2(string Value);
    public record struct C3(Vector3 Value);

    public static void Run(World world)
    {
        var dynEntity = DynEntityRef.Create(
            world.CreateInBucketHost(Bundle.Create(new C1(114514))),
            new WorldEntityCreators.Bucket(world));

        Console.WriteLine(dynEntity.Boxed);
        dynEntity.Add(new C2("Hello world"));
        Console.WriteLine(dynEntity.Boxed);
        dynEntity.Add(new C3(Vector3.One));
        Console.WriteLine(dynEntity.Boxed);
    }
}