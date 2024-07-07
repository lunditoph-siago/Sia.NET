using System.Numerics;
using Sia;

namespace Sia_Examples
{
    namespace ComponentBundle
    {
        public partial record struct Position([Sia] Vector3 Value);
        public partial record struct Rotation([Sia] Quaternion Value);
        public partial record struct Scale([Sia] Vector3 Value);

        [SiaBundle]
        public partial record struct Transform(Position Position, Rotation Rotation, Scale Scale);

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

        [SiaBundle]
        public partial record struct GameObject(ObjectId Id, Name Name);

        public record struct HP([Sia] int Value);

        public record TestObjectBundle() : DynBundle(
            new DynBundle()
                .Add(new HP(100)),
            new Transform {
                Position = new Position {
                    Value = Vector3.Zero
                },
                Rotation = new Rotation {
                    Value = Quaternion.Identity
                },
                Scale = new Scale {
                    Value = Vector3.One
                }
            },
            new GameObject {
                Id = new(0),
                Name = "Test"
            });
    }

    public static partial class Example5_ComponentBundle
    {
        public static void Run(World world)
        {
            var entity = world.CreateInArrayHost()
                .AddBundle(new ComponentBundle.TestObjectBundle());

            Console.WriteLine("Entity 1:");
            Console.WriteLine(entity.Get<ComponentBundle.Name>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.HP>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Position>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Rotation>().Value);
            Console.WriteLine(entity.Get<ComponentBundle.Scale>().Value);

            Console.WriteLine();

            var entity2 = world.CreateInArrayHost()
                .AddBundle(new ComponentBundle.TestObjectBundle())
                .Remove<ComponentBundle.Scale>();

            Console.WriteLine("Entity 2:");
            Console.WriteLine(entity2.Get<ComponentBundle.Name>().Value);
            Console.WriteLine(entity2.Get<ComponentBundle.HP>().Value);
            Console.WriteLine(entity2.Get<ComponentBundle.Position>().Value);
            Console.WriteLine(entity2.Get<ComponentBundle.Rotation>().Value);
            Console.WriteLine(entity2.Contains<ComponentBundle.Scale>());

            Console.WriteLine(entity2.RemoveBundle<ComponentBundle.GameObject>().Boxed);
        }
    }
}