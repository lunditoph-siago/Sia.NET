namespace Sia.Examples;

using System.Numerics;

public static partial class Example5_ComponentBundle
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id)
            => new(id);
    }

    public record struct Name([SiaProperty] string Value)
    {
        public static implicit operator Name(string name)
            => new(name);
    }

    public record struct Position([SiaProperty] Vector3 Value);
    public record struct Rotation([SiaProperty] Quaternion Value);
    public record struct Scale([SiaProperty] Vector3 Value);

    public record struct TransformBundle(
        Position Position, Rotation Rotation, Scale Scale) : IComponentBundle
    {
        public TransformBundle()
            : this(
                Position: new(),
                Rotation: new(Quaternion.Identity),
                Scale: new(Vector3.One))
        {
        }
    }
    
    public record struct ObjectBundle(
        Sid<ObjectId> Id, Name Name, TransformBundle TransformBundle) : IComponentBundle
    {
        public ObjectBundle()
            : this(
                Id: new(),
                Name: new(),
                TransformBundle: new())
        {
        }
    }
    
    public record struct HP([SiaProperty] int Value);
    
    public static class TestObject
    {
        public static EntityRef Create(World world)
            => world.CreateInHashHost(Tuple.Create(
                new ObjectBundle { Name = "TestObject" },
                new HP(100)
            ));
    }

    public static void Run()
    {
        var world = new World();

        var entity = TestObject.Create(world);
        Console.WriteLine(entity.Get<Name>().Value);
        Console.WriteLine(entity.Get<HP>().Value);
        Console.WriteLine(entity.Get<Position>().Value);
        Console.WriteLine(entity.Get<Rotation>().Value);
        Console.WriteLine(entity.Get<Scale>().Value);
    }
}