namespace Sia.Examples;

using System.Runtime.CompilerServices;

public record struct Position
{
    public float X;
    public float Y;
    public float Z;
    public List<int> ManagedTest;

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
        Console.WriteLine("Size: " + desc.Size);
        Console.WriteLine("Component offsets:");

        desc.TryGetOffset<Position>(out var offset);
        Console.WriteLine("\tPosition: " + offset + ", Value: " + Unsafe.AsRef<Position>((void*)(ptr + offset)));

        desc.TryGetOffset<Rotation>(out offset);
        Console.WriteLine("\tRotation: " + offset + ", Value: " + Unsafe.AsRef<Rotation>((void*)(ptr + offset)));

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

    private static void TestWorldGroupCache()
    {
        Console.WriteLine("== Test WorldGroupCache ==");

        var world = new World();

        var e1Ref = EntityFactory<TestEntity>.ManagedHeap.Create();
        var e2Ref = EntityFactory<TestEntity2>.ManagedHeap.Create();

        world.Add(e1Ref);
        world.Add(e2Ref);

        var handle = WorldGroupCache.Acquire<TypeUnion<Position>>(world);
        Console.WriteLine(handle.Group.Contains(e1Ref));
        Console.WriteLine(handle.Group.Contains(e2Ref));

        var handle2 = WorldGroupCache.Acquire<TypeUnion<Position>>(world);
        Console.WriteLine(handle.Cache == handle2.Cache);
    }

    private class PositionPrintSystem : SystemBase
    {
        public PositionPrintSystem()
        {
            Matcher = Matchers.From<TypeUnion<Position>>();
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
            Matcher = new TypeUnion<Position>().ToMatcher();
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

        var e1Ref = EntityFactory<TestEntity>.ManagedHeap.Create(new() {
            Position = new Position {
                X = 1,
                Y = 2,
                Z = 3
            }
        });
        var e2Ref = EntityFactory<TestEntity>.ManagedHeap.Create(new() {
            Position = new Position {
                X = -1,
                Y = -2,
                Z = -3
            }
        });

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
        DoTest(ManagedHeapStorage<int>.Instance);
        DoTest(PooledStorage<int>.Create(ManagedHeapStorage<int>.Instance, 2));
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
            var factory = new EntityFactory<TestEntity, TStorage>(storage);
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
        DoTest(ManagedHeapStorage<TestEntity>.Instance);
        DoTest(PooledStorage<TestEntity>.Create(ManagedHeapStorage<TestEntity>.Instance, 2));
        DoTest(new VariableStorage<TestEntity, SparseBufferStorage<TestEntity>>(() => new(1)));
        //DoTest(UnmanagedHeapStorage<TestEntity>.Instance);
    }

    public static void Run()
    {
        TestEntityDescriptor();
        TestGroup();
        TestScheduler();
        TestDispatcher();
        TestTypeUnion();
        TestMatcher();
        TestWorldGroupCache();
        TestSystem();
        TestStorages();
        TestEntityFactory();
    }
}