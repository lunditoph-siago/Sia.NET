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
    public static readonly TestEntity DefaultTestEntity = HList.Create(
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

        var e1Ref = world.GetBucketHost<TestEntity>().Create();
        var e2Ref = world.GetBucketHost<TestEntity2>().Create();

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

    private static void TestStorages()
    {
        Console.WriteLine("== Test Storages ==");

        static void DoTest(IStorage<int> storage)
        {
            var pointers = new List<Pointer<int>>();
            
            for (int c = 0; c < 10; c++) {
                if (Random.Shared.NextSingle() < 0.5) {
                    var count = Random.Shared.Next(1, 30);
                    for (int i = 0; i < count; ++i) {
                        pointers.Add(storage.Allocate());
                    }
                }
                else {
                    while (pointers.Count > 0) {
                        int index = Random.Shared.Next(0, pointers.Count);
                        pointers[index].Dispose();
                        pointers.RemoveAt(index);
                    }
                }
            }

            while (pointers.Count > 0) {
                int index = Random.Shared.Next(0, pointers.Count);
                pointers[index].Dispose();
                pointers.RemoveAt(index);
            }
            return;
        }

        DoTest(new ArrayBufferStorage<int>(5120));
        DoTest(new SparseBufferStorage<int>(5120));
        DoTest(new HashBufferStorage<int>());

        Console.WriteLine("Finished");
    }

    private static void TestEntityFactory()
    {
        Console.WriteLine("== Test Entity Factory ==");

        static void DoTest<TStorage>(TStorage storage)
            where TStorage : class, IStorage<HList<Entity, TestEntity>>
        {
            Console.WriteLine($"[{storage}]");
            var factory = new StorageEntityHost<TestEntity, TStorage>(storage);
            var e1 = factory.Create(DefaultTestEntity);
            var e2 = factory.Create();
            var e3 = factory.Create();
            Console.WriteLine(e1.Get<Position>());
            Console.WriteLine(e1.Get<Rotation>());
            Console.WriteLine(e1.Get<Scale>());
            Console.WriteLine(e2.Get<Position>());
            Console.WriteLine(e3.Get<Position>());
            e1.Dispose();
            e2.Dispose();
            e3.Dispose();
            var e4 = factory.Create();
            Console.WriteLine(e4.Get<Position>());
            var e5 = factory.Create();
            Console.WriteLine(e5.Get<Position>());
            e4.Dispose();
            e5.Dispose();
        }

        DoTest(new ArrayBufferStorage<HList<Entity, TestEntity>>());
        DoTest(new SparseBufferStorage<HList<Entity, TestEntity>>());
        DoTest(new HashBufferStorage<HList<Entity, TestEntity>>());
    }

    public static void Run()
    {
        TestEntityDescriptor();
        TestDispatcher();
        TestTypeUnion();
        TestMatcher();
        TestWorldQuery();
        TestStorages();
        TestEntityFactory();
    }
}