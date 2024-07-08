namespace Sia_Examples;

using System.Runtime.CompilerServices;
using Sia;

using TestEntity = Sia.HList<Position, Sia.HList<Rotation, Sia.HList<Scale, Sia.EmptyHList>>>;
using TestEntity2 = Sia.HList<Position, Sia.HList<Rotation, Sia.EmptyHList>>;

public record struct Position(float X, float Y, float Z)
{
    public List<int> ManagedTest;

    public readonly record struct Set(float X, float Y, float Z) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            ref var pos = ref target.Get<Position>();
            pos.X = X;
            pos.Y = Y;
            pos.Z = Z;
        }
    }
}

public record struct Rotation
{
    public float Angle;
}

public record struct Scale
{
    public float X;
    public float Y;
    public float Z;
}

public unsafe static class Tests
{
    public static readonly TestEntity DefaultTestEntity = HList.From(
        new Position(1, 2, 3),
        new Rotation { Angle = 2 },
        new Scale() {
            X = 1,
            Y = 2,
            Z = 3
        }
    );

    private unsafe static void TestEntityDescriptor()
    {
        Console.WriteLine("== Test Entity Descriptor ==");

        var e = DefaultTestEntity;

        var ptr = (IntPtr)Unsafe.AsPointer(ref e);

        var desc = EntityDescriptor.Get<TestEntity>();
        Console.WriteLine("Component offsets:");

        var positionOffset = desc.GetOffset<Position>();
        Console.WriteLine("\tPosition: " + positionOffset + ", Value: " + Unsafe.AsRef<Position>((void*)(ptr + positionOffset)));

        var rotationOffset = desc.GetOffset<Rotation>();
        Console.WriteLine("\tRotation: " + rotationOffset + ", Value: " + Unsafe.AsRef<Rotation>((void*)(ptr + rotationOffset)));

        var scaleOffset = desc.GetOffset<Scale>();
        Console.WriteLine("\tScale: " + scaleOffset + ", Value: " + *(Scale*)(ptr + scaleOffset));
    }

    private readonly record struct TestCommand(int Target) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            Console.WriteLine("Command: " + target);
        }
    }

    private static void TestDispatcher()
    {
        Console.WriteLine("== Test Dispatcher ==");

        var disp = new Dispatcher<int, IEvent>();

        disp.Listen((int target, in TestCommand e) => {
            Console.WriteLine("Command: " + target);
            return target == 2;
        });

        disp.Send(1, new TestCommand { });
        disp.Send(1, new TestCommand { });
        disp.Send(2, new TestCommand { });
        disp.Send(2, new TestCommand { });
    }

    private static void TestTypeUnion()
    {
        Console.WriteLine("== Test TypeUnion ==");

        var u1 = new TypeUnion<int, string, uint>();
        var u2 = new TypeUnion<string, uint, int>();

        Console.WriteLine(u1.GetHashCode() == u2.GetHashCode());

        var dict = new Dictionary<ITypeUnion, int>() {
            { new TypeUnion<int, string>(), 1 }
        };
        Console.WriteLine(dict[new TypeUnion<string, int>()]);

        dict.Add(new TypeUnion<string, string>(), 2);
        Console.WriteLine(dict[new TypeUnion<string>()]);
        Console.WriteLine(dict[new TypeUnion<string, string, string>()]);
    }

    private static void TestMatcher()
    {
        Console.WriteLine("== Test Matcher ==");

        Console.WriteLine(new TypeUnion<int>().ToMatcher().Equals(new TypeUnion<long>().ToMatcher()));
        Console.WriteLine(
            new TypeUnion<long, int>().ToMatcher()
                .With(new TypeUnion<int>()).Equals(
                    new TypeUnion<int, long>().ToMatcher()
                        .With(new TypeUnion<int>())));
    }

    private static void TestWorldQuery()
    {
        Console.WriteLine("== Test World.Query ==");

        var world = new World();

        var e1Ref = world.GetArrayHost<TestEntity>().Create();
        var e2Ref = world.GetArrayHost<TestEntity2>().Create();

        var query1 = world.Query<TypeUnion<Position>>();
        var group = new List<Entity>();
        foreach (var entity in query1) {
            group.Add(entity);
        }

        Console.WriteLine(group.Contains(e1Ref));
        Console.WriteLine(group.Contains(e2Ref));

        var query2 = world.Query<TypeUnion<Position>>();
        Console.WriteLine(query1 == query2);
    }

    private class PositionPrintSystem : SystemBase
    {
        public PositionPrintSystem()
        {
            Matcher = Matchers.Of<Position>();
        }

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                Console.WriteLine(entity.Get<Position>());
            }
        }
    }

    private class PositionChangeListenSystem : SystemBase
    {
        public PositionChangeListenSystem()
        {
            Matcher = Matchers.Of<Position>();
            Trigger = EventUnion.Of<Position.Set>();
        }
        
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                Console.WriteLine("--> Changed: " + entity.Get<Position>());
            }
        }
    }

    private class PositionSystems : SystemBase
    {
        public PositionSystems()
        {
            Children = SystemChain.Empty
                .Add<PositionPrintSystem>()
                .Add<PositionChangeListenSystem>();
        }
    }

    public static void Run()
    {
        TestEntityDescriptor();
        TestDispatcher();
        TestTypeUnion();
        TestMatcher();
        TestWorldQuery();
    }
}