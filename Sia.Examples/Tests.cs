namespace Sia.Example.Tests;

public record struct Position
{
    public float X;
    public float Y;
    public float Z;

    public class Set : Command<Set>
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }

        public static Set Create(float x, float y, float z)
        {
            var cmd = CreateRaw();
            cmd.X = x;
            cmd.Y = y;
            cmd.Z = z;
            return cmd;
        }

        public override void Execute(in EntityRef target)
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

public static class Tests
{
    private unsafe static void TestEntityDescriptor()
    {
        Console.WriteLine("== Test Entity Descriptor ==");

        var e = new TestEntity {
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
        var ptr = (IntPtr)(&e);
        Console.WriteLine(e.Scale);

        var desc = EntityDescriptor.Get<TestEntity>();
        Console.WriteLine("Size: " + desc.Size);
        Console.WriteLine("Component offsets:");

        desc.TryGetOffset<Position>(out var offset);
        Console.WriteLine("\tPosition: " + offset + ", Value: " + *((Position*)(ptr + offset)));

        desc.TryGetOffset<Rotation>(out offset);
        Console.WriteLine("\tRotation: " + offset + ", Value: " + *((Rotation*)(ptr + offset)));

        desc.TryGetOffset<Scale>(out offset);
        Console.WriteLine("\tScale: " + offset + ", Value: " + *((Scale*)(ptr + offset)));
    }

    private static void TestGroup()
    {
        Console.WriteLine("== Test Group ==");

        var g = new Group<int> { 1, 2, 3 };
        Console.WriteLine(g.Contains(1));
        Console.WriteLine(g.Contains(2));
        Console.WriteLine(g.Contains(3));

        Console.WriteLine(g.Remove(1));
        Console.WriteLine(g.Contains(1));

        foreach (ref int v in g.AsSpan()) {
            Console.WriteLine(v);
        }
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
            sched.RemoveTask(task2!);
            return false;
        });

        var task3 = sched.CreateTask(() => {
            Console.WriteLine("Call once 2");
            return true;
        }, new[] {task1});

        var task4 = sched.CreateTask(() => {
            Console.WriteLine("Infinite 2");
            return false;
        }, new[] {task1});

        sched.Tick();
        sched.Tick();
        sched.Tick();
        sched.Tick();
        sched.Tick();
    }

    private class TestCommand : Command<TestCommand, int>
    {
        public static TestCommand Create()
            => CreateRaw();

        public override void Execute(in int target)
        {
            Console.WriteLine("Command: " + target);
        }
    }

    private static void TestDispatcher()
    {
        Console.WriteLine("== Test Dispatcher ==");

        var disp = new Dispatcher<int>();

        disp.Listen<TestCommand>((in int target, IEvent e) => {
            Console.WriteLine("Command: " + target);
            return target == 2;
        });

        disp.Listen(1, (in int target, IEvent e) => {
            Console.WriteLine("Command: " + target);
            return false;
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

        var dict = new Dictionary<ITypeUnion, int>(new TypeUnionComparer()) {
            { new TypeUnion<int, string>(), 1 }
        };
        Console.WriteLine(dict[new TypeUnion<string, int>()]);

        dict.Add(new TypeUnion<string, string>(), 2);
        Console.WriteLine(dict[new TypeUnion<string>()]);
        Console.WriteLine(dict[new TypeUnion<string, string, string>()]);
    }

    private class PositionPrintSystem : SystemBase
    {
        public PositionPrintSystem()
        {
            Matcher = new TypeUnion<Position>();
        }

        public override void Execute(World world, Scheduler scheduler, in EntityRef entity)
        {
            Console.WriteLine(entity.Get<Position>());
        }
    }

    private class PositionChangeListenSystem : SystemBase
    {
        public PositionChangeListenSystem()
        {
            Matcher = new TypeUnion<Position>();
            Trigger = new EventUnion<Position.Set>();
        }
        
        public override void Execute(World world, Scheduler scheduler, in EntityRef entity)
        {
            Console.WriteLine("--> Changed: " + entity.Get<Position>());
        }
    }

    private class PositionSystems : SystemBase
    {
        public PositionSystems()
        {
            Children = new SystemUnion<PositionPrintSystem, PositionChangeListenSystem>();
        }
    }

    private static void TestSystem()
    {
        Console.WriteLine("== Test System ==");

        var world = new World();
        var scheduler = new Scheduler();

        new PositionSystems().Register(world, scheduler);

        var entity1 = new TestEntity {
            Position = new Position {
                X = 1,
                Y = 2,
                Z = 3
            }
        };

        var entity2 = new TestEntity {
            Position = new Position {
                X = -1,
                Y = -2,
                Z = -3
            }
        };

        var e1Ref = EntityRef.Create(ref entity1);
        var e2Ref = EntityRef.Create(ref entity2);

        world.Add(e1Ref);
        world.Add(e2Ref);
        scheduler.Tick();
        scheduler.Tick();

        world.Modify(e1Ref, Position.Set.Create(4, 5, 6));
        world.Modify(e2Ref, Position.Set.Create(-4, -5, -6));
        scheduler.Tick();
    }

    private static void TestStorages()
    {
        Console.WriteLine("== Test Storages ==");

        static void DoTest(IStorage storage)
        {
            var ptr1 = storage.Allocate();
            var ptr2 = storage.Allocate();
            storage.Release(ptr1);
            storage.Release(ptr2);
            var ptr3 = storage.Allocate();
            var ptr4 = storage.Allocate();
            var ptr5 = storage.Allocate();
            storage.Release(ptr3);
            storage.Release(ptr5);
            storage.Release(ptr4);
        }

        DoTest(new PoolStorage<int>());
        DoTest(new NativeStorage<int>());
        DoTest(new PooledNativeStorage<int>(2));

        Console.WriteLine("Finished");
    }

    private static void TestEntityFactory()
    {
        Console.WriteLine("== Test Entity Factory ==");

        static void DoTest(IStorage<TestEntity> storage)
        {
            var factory = new EntityFactory<TestEntity>(storage);
            var e1 = factory.Create();
            var e2 = factory.Create();
            var e3 = factory.Create();
            Console.WriteLine(e1.Get<Position>());
            Console.WriteLine(e1.Get<Rotation>());
            Console.WriteLine(e1.Get<Scale>());
            e1.Destroy();
            e2.Destroy();
            e3.Destroy();
            var e4 = factory.Create();
            var e5 = factory.Create();
            e4.Destroy();
            e5.Destroy();
        }

        DoTest(new PoolStorage<TestEntity>());
        DoTest(new NativeStorage<TestEntity>());
        DoTest(new PooledNativeStorage<TestEntity>(2));
    }

    public static void Run()
    {
        TestEntityDescriptor();
        TestGroup();
        TestScheduler();
        TestDispatcher();
        TestTypeUnion();
        TestSystem();
        TestStorages();
        TestEntityFactory();
    }
}