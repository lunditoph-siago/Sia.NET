namespace Sia_Examples;

using System.Runtime.CompilerServices;
using Sia;

public record struct Position
{
    public float X;
    public float Y;
    public float Z;
    public List<int> ManagedTest;

    public readonly record struct Set(float X, float Y, float Z) : ICommand
    {
        public void Execute(World world, in EntityRef target)
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

public record struct TestEntity
{
    public Position Position;
    public Rotation Rotation;
    public Scale Scale;
}

public record struct TestEntity2
{
    public Position Position;
    public Rotation Rotation;
}

public unsafe static class Tests
{
    public static readonly TestEntity DefaultTestEntity = new() {
        Position = new() {
            X = 1,
            Y = 2,
            Z = 3
        },
        Rotation = new() {
            Angle = 2
        },
        Scale = new() {
            X = 1,
            Y = 2,
            Z = 3
        }
    };

    private unsafe static void TestEntityDescriptor()
    {
        Console.WriteLine("== Test Entity Descriptor ==");

        var e = DefaultTestEntity;

        var ptr = (IntPtr)Unsafe.AsPointer(ref e);
        Console.WriteLine(e.Scale);

        var desc = EntityDescriptor.Get<TestEntity>();
        Console.WriteLine("Component offsets:");

        desc.TryGetOffset<Position>(out var offset);
        Console.WriteLine("\tPosition: " + offset + ", Value: " + Unsafe.AsRef<Position>((void*)(ptr + offset)));

        desc.TryGetOffset<Rotation>(out offset);
        Console.WriteLine("\tRotation: " + offset + ", Value: " + Unsafe.AsRef<Rotation>((void*)(ptr + offset)));

        desc.TryGetOffset<Scale>(out offset);
        Console.WriteLine("\tScale: " + offset + ", Value: " + *((Scale*)(ptr + offset)));
    }

    private static void TestScheduler()
    {
        Console.WriteLine("== Test Scheduler ==");
        
        var sched = new Scheduler();

        var task1 = sched.CreateTask(() => {
            Console.WriteLine("Infinite 1");
            return false;
        });

        Scheduler.TaskGraphNode? task2 = null;
        task2 = sched.CreateTask(() => {
            Console.WriteLine("Call once 1");
            task2!.Dispose();
            return false;
        });

        var task3 = sched.CreateTask(() => {
            Console.WriteLine("Call once 2");
            return true;
        }, [task1]);

        var task4 = sched.CreateTask(() => {
            Console.WriteLine("Infinite 2");
            return false;
        }, [task1]);

        sched.Tick();
        sched.Tick();
        sched.Tick();
        sched.Tick();
        sched.Tick();
    }

    private readonly record struct TestCommand(int Target) : ICommand
    {
        public void Execute(World world, in EntityRef target)
        {
            Console.WriteLine("Command: " + target);
        }
    }

    private static void TestDispatcher()
    {
        Console.WriteLine("== Test Dispatcher ==");

        var disp = new Dispatcher<int, IEvent>();

        disp.Listen((in int target, in TestCommand e) => {
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

        var e1Ref = world.GetManagedHeapHost<TestEntity>().Create();
        var e2Ref = world.GetManagedHeapHost<TestEntity2>().Create();

        var query1 = world.Query<TypeUnion<Position>>();
        var group = new List<EntityRef>();
        query1.ForEach(group.Add);

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

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                Console.WriteLine(entity.Get<Position>());
            });
        }
    }

    private class PositionChangeListenSystem : SystemBase
    {
        public PositionChangeListenSystem()
        {
            Matcher = Matchers.Of<Position>();
            Trigger = EventUnion.Of<Position.Set>();
        }
        
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                Console.WriteLine("--> Changed: " + entity.Get<Position>());
            });
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

    private static void TestSystem()
    {
        Console.WriteLine("== Test System ==");

        var world = new World();
        var scheduler = new Scheduler();

        world.RegisterSystem<PositionSystems>(scheduler);

        var e1Ref = world.GetManagedHeapHost<TestEntity>().Create( new() {
            Position = new Position {
                X = 1,
                Y = 2,
                Z = 3
            }
        });
        var e2Ref = world.GetManagedHeapHost<TestEntity>().Create(new() {
            Position = new Position {
                X = -1,
                Y = -2,
                Z = -3
            }
        });

        scheduler.Tick();
        scheduler.Tick();

        world.Modify(e1Ref, new Position.Set(4, 5, 6));
        world.Modify(e2Ref, new Position.Set(-4, -5, -6));
        scheduler.Tick();
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
        DoTest(ManagedHeapStorage<int>.Instance);
        DoTest(UnmanagedHeapStorage<int>.Instance);

        Console.WriteLine("Finished");
    }

    private static void TestEntityFactory()
    {
        Console.WriteLine("== Test Entity Factory ==");

        static void DoTest<TStorage>(TStorage storage)
            where TStorage : class, IStorage<TestEntity>
        {
            Console.WriteLine($"[{storage}]");
            var factory = new EntityHost<TestEntity, TStorage>(storage);
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

        DoTest(new ArrayBufferStorage<TestEntity>(512));
        DoTest(new SparseBufferStorage<TestEntity>(512));
        DoTest(new HashBufferStorage<TestEntity>());
        DoTest(ManagedHeapStorage<TestEntity>.Instance);
        //DoTest(UnmanagedHeapStorage<TestEntity>.Instance);
    }

    public static void Run()
    {
        TestEntityDescriptor();
        TestScheduler();
        TestDispatcher();
        TestTypeUnion();
        TestMatcher();
        TestWorldQuery();
        TestSystem();
        TestStorages();
        TestEntityFactory();
    }
}