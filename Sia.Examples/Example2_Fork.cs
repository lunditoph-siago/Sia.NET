namespace Sia_Examples;

using Sia;
using Sia.Reactors;

public static partial class Example2_Fork
{
    public readonly record struct Process(string Name);

    public readonly record struct Uid(int Value)
    {
        public static implicit operator Uid(int value) => new(value);
    }

    public readonly record struct Pid(int Value)
    {
        public static implicit operator Pid(int value) => new(value);
    }

    public sealed class ProcessTree {}

    public readonly record struct Owner(Uid Value);

    [SiaBundle]
    public partial record struct ProcessImage(Process Process, Owner Owner);

    public readonly record struct Sandboxed;

    [SiaBundle]
    public partial record struct ContainerImage(Process Process, Owner Owner, Sandboxed Sandboxed)
    {
        public ContainerImage() : this(
            new Process("container-init"),
            new Owner(new Uid(1000)),
            default) {}
    }

    private static void RunFork(World world)
    {
        Console.WriteLine("-- Scene 1: fork() from a container image --");

        var proc1 = world.Create().AddBundle(new ContainerImage());
        Console.WriteLine(proc1.Get<Process>().Name);
        Console.WriteLine("owner uid: " + proc1.Get<Owner>().Value.Value);
        Console.WriteLine("sandboxed: " + proc1.Contains<Sandboxed>());

        var proc2 = world.Create()
            .AddBundle(new ContainerImage())
            .Remove<Sandboxed>();
        Console.WriteLine("graduated out of the sandbox: " + !proc2.Contains<Sandboxed>());
        Console.WriteLine(proc2.RemoveBundle<ProcessImage>().Boxed);
    }

    public static class Init
    {
        public static Entity Create(
            World world, string name, Uid uid, Pid pid, Entity? parent = null)
            => world.Create(HList.From(
                new Process(name),
                new Node<ProcessTree>(parent),
                Sid.From(uid),
                Sid.From(pid)));
    }

    public sealed class ProcessCountByOwnerSystem() : SystemBase(
        Matchers.Of<Aggregation<Uid>>(),
        EventUnion.Of<Aggregation<Uid>.EntityAdded, Aggregation<Uid>.EntityRemoved>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var aggr = ref entity.Get<Aggregation<Uid>>();
                Console.WriteLine($"uid {aggr.Id.Value} now owns {aggr.Group.Count} process(es)");
            }
        }
    }

    private static void RunProcessTree(World world)
    {
        Console.WriteLine();
        Console.WriteLine("-- Scene 2: pstree, cgroups and /proc --");

        world.AcquireAddon<Hierarchy<ProcessTree>>();
        world.AcquireAddon<Aggregator<Uid>>();
        var mapper = world.AcquireAddon<Mapper<Pid>>();

        var stage = SystemChain.Empty
            .Add<ProcessCountByOwnerSystem>()
            .CreateStage(world);

        var init = Init.Create(world, "init", uid: 0, pid: 1);
        var sshd = Init.Create(world, "sshd", uid: 0, pid: 100, parent: init);
        var bash = Init.Create(world, "bash", uid: 1000, pid: 101, parent: sshd);
        var vim = Init.Create(world, "vim", uid: 1000, pid: 102, parent: bash);

        stage.Tick();

        Console.WriteLine("pstree (children of sshd):");
        foreach (var child in sshd.Get<Node<ProcessTree>>().Children) {
            Console.WriteLine("  " + child.Get<Process>().Name);
        }

        Console.WriteLine("/proc/102 -> " + (mapper[102] == vim));

        Console.WriteLine("freezing sshd's cgroup...");
        _ = new Node<ProcessTree>.View(sshd, world) {
            IsSelfEnabled = false
        };
        Console.WriteLine("bash frozen: " + !bash.Get<Node<ProcessTree>>().IsEnabled);
        Console.WriteLine("vim frozen: " + !vim.Get<Node<ProcessTree>>().IsEnabled);

        Console.WriteLine("vim exits; pid 102 gets reused by a fresh nano process...");
        vim.Destroy();
        var nano = Init.Create(world, "nano", uid: 1000, pid: 103, parent: bash);
        nano.SetSid(new Pid(102));
        stage.Tick();
        Console.WriteLine("/proc/102 -> " + (mapper[102] == nano ? "nano (reused)" : "someone else"));

        Console.WriteLine("session leader sshd exits; the whole session goes with it...");
        sshd.Destroy();
        Console.WriteLine("remaining processes: " + world.Count);

        stage.Dispose();
    }

    public static void Run(World world)
    {
        RunFork(world);
        RunProcessTree(world);
    }
}
