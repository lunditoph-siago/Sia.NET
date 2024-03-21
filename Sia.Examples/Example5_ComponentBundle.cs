using System.Numerics;
using Sia;

namespace Sia_Examples
{
    namespace ComponentBundle
    {
        public partial record struct Position([Sia] Vector3 Value);
        public partial record struct Rotation([Sia] Quaternion Value);
        public partial record struct Scale([Sia] Vector3 Value);

        public readonly record struct ObjectId(int Value)
        {
            public static implicit operator ObjectId(int id)
                => new(id);
        }

        public record struct Name([Sia] string Value)
        {
            public static implicit operator Name(string name)
                => new(name);
        }
            
        public static class BundleExtensions
        {
            public static EntityRef AddTransformBundle(this EntityRef entity)
                => entity.AddBundle(HList.Create(
                    new Position(),
                    new Rotation(Quaternion.Identity),
                    new Scale(Vector3.One)));

            public static EntityRef AddObjectBundle(this EntityRef entity, ObjectId id, string name)
                => entity.AddBundle(HList.Create(
                    new Sid<ObjectId>(id),
                    new Name(name)));
        }

        public record struct HP([Sia] int Value);
        
        public static class TestObject
        {
            public static EntityRef Create(World world)
                => world.CreateInArrayHost(HList.Create(
                    new HP(100)))
                    .AddTransformBundle()
                    .AddObjectBundle(new(0), "Test");
        }
    }

    public static partial class Example5_ComponentBundle
    {
        public static void Run(World world)
        {
            var entity = ComponentBundle.TestObject.Create(world);
            Console.WriteLine(entity.Get<ComponentBundle.Name>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.HP>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Position>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Rotation>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Scale>().Value);
        }
    }
}