using System.Runtime.InteropServices;

using Sia;

public record struct Position
{
    public float X;
    public float Y;
    public float Z;
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

public static class Program
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

    private record TestCommand : IExecutableCommand<int>
    {
        public void Execute(int target)
        {
            Console.WriteLine("Command: " + target);
        }

        public void Dispose()
        {
        }
    }

    private static void TestDispatcher()
    {
        Console.WriteLine("== Test Dispatcher ==");

        var disp = new Dispatcher<int>();

        disp.Listen<TestCommand>((target, cmd) => {
            Console.WriteLine("Command: " + target);
            return target == 2;
        });

        disp.Listen(1, (target, cmd) => {
            Console.WriteLine("Command: " + target);
            return false;
        });

        disp.Send(1, new TestCommand { });
        disp.Send(1, new TestCommand { });
        disp.Send(2, new TestCommand { });
        disp.Send(2, new TestCommand { });
    }

    private static void TestTypeSet()
    {
        Console.WriteLine("== Test TypeSet ==");

        var u1 = new TypeSet<int, string, uint>();
        var u2 = new TypeSet<string, uint, int>();

        Console.WriteLine(u1.ProxyHash == u2.ProxyHash);

        var dict = new Dictionary<ITypeUnion, int>(new TypeSetComparer());

        dict.Add(new TypeSet<int, string>(), 1);
        Console.WriteLine(dict[new TypeSet<string, int>()]);

        dict.Add(new TypeSet<string, string>(), 2);
        Console.WriteLine(dict[new TypeSet<string>()]);
        Console.WriteLine(dict[new TypeSet<string, string, string>()]);
    }

    public unsafe static void Main()
    {
        TestEntityDescriptor();
        TestGroup();
        TestScheduler();
        TestDispatcher();
        TestTypeSet();
    }
}