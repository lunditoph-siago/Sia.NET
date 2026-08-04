namespace Sia_Examples;

using System.Collections.Concurrent;
using Sia;

public static partial class Example3_Htop
{
    public readonly record struct Proc(string Name);
    public partial record struct Cpu([Sia] float Load);
    public partial record struct Mem([Sia] int UsedMb);

    public sealed class Clock : IAddon
    {
        public float Delta { get; set; }
    }

    public interface IMetricsExporter : IAddon
    {
        void Export(string line);
    }

    public sealed class JsonExporter : IMetricsExporter
    {
        public void Export(string line) => Console.WriteLine("[json] " + line);
    }

    public sealed class PrometheusExporter : IMetricsExporter
    {
        public void Export(string line) => Console.WriteLine("[prom] " + line);
    }

    private static void RunExporters(World world)
    {
        Console.WriteLine("-- Scene 1: which exporters are wired up? --");

        var json = world.AcquireAddon<JsonExporter>();
        var prom = world.AddAddon<PrometheusExporter>();
        var any = world.GetAddon<IMetricsExporter>();
        Console.WriteLine("found by interface: " + (any == json || any == prom));

        foreach (var exporter in world.GetAddons<IMetricsExporter>()) {
            exporter.Export("cpu_load 0.42");
        }
    }

    public sealed class LoadUpdateSystem() : SystemBase(
        Matchers.Of<Cpu, Mem>())
    {
        private Clock _clock = null!;

        public override void Initialize(World world)
            => _clock = world.GetAddon<Clock>();

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var cpu = ref entity.Get<Cpu>();
                ref var mem = ref entity.Get<Mem>();
                cpu.Load = Math.Clamp(
                    cpu.Load + (Random.Shared.NextSingle() - 0.4f) * _clock.Delta, 0, 1);
                mem.UsedMb = Math.Max(0, mem.UsedMb + Random.Shared.Next(-2, 5));
            }
        }
    }

    public sealed class HtopExtractSystem() : ExtractSystemBase(
        matcher: Matchers.Any, extractMatcher: Matchers.Of<Proc, Cpu, Mem>())
    {
        public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
        {
            Console.WriteLine("  [htop:extract, live] " + extract.Count + " process(es)");
            foreach (var entity in extract) {
                var proc = entity.Get<Proc>();
                var cpu = entity.Get<Cpu>();
                if (cpu.Load > 0.8f) {
                    Console.WriteLine($"    {proc.Name,-16} cpu {cpu.Load:P0}  <- hot");
                }
            }
        }
    }

    public readonly record struct ProcessSnapshot(int EntityId, float Cpu, int MemUsedMb);

    public sealed class HtopSnapshotSystem : SnapshotExtractSystem<ProcessSnapshot>
    {
        protected override IEntityMatcher ExtractMatcher => Matchers.Of<Proc, Cpu, Mem>();

        protected override ProcessSnapshot Extract(Entity entity)
            => new(entity.Id.Value, entity.Get<Cpu>().Load, entity.Get<Mem>().UsedMb);

        protected override void Render(ReadOnlySpan<ProcessSnapshot> data)
        {
            Console.WriteLine($"  [top -b, frozen] {data.Length} process(es)");
            foreach (ref readonly var p in data) {
                Console.WriteLine($"    pid {p.EntityId,-6} cpu {p.Cpu:P0}  mem {p.MemUsedMb,4} MB");
            }
        }
    }

    private static void RunLiveMonitor(World kernel)
    {
        Console.WriteLine();
        Console.WriteLine("-- Scene 2: htop watches the kernel from another thread --");

        var clock = kernel.AcquireAddon<Clock>();
        var sub = kernel.AcquireAddon<SubWorldAddon>().SubWorld;

        using var kernelStage = SystemChain.Empty
            .Add<LoadUpdateSystem>()
            .CreateStage(kernel);

        var snapshotSystem = new HtopSnapshotSystem();
        using var monitorStage = SystemChain.Empty
            .Add<HtopExtractSystem>()
            .Add<HtopSnapshotSystem>(() => snapshotSystem)
            .CreateStage(sub.World);

        using var alerts = new ExtractChannel<int>(capacity: 64);

        for (var i = 0; i < 5; i++) {
            kernel.Create(HList.From(
                new Proc($"worker-{i}"),
                new Cpu(0.1f),
                new Mem(50)));
        }

        using var frameReady = new AutoResetEvent(false);
        using var frameDone = new AutoResetEvent(false);
        var running = true;

        var htopThread = new Thread(() => {
            while (true) {
                frameReady.WaitOne();
                if (!running) {
                    return;
                }

                sub.Tick(monitorStage);

                var drained = alerts.Drain(pid => Console.WriteLine($"    ALERT: pid {pid} is hammering the CPU"));
                if (drained > 0) {
                    Console.WriteLine($"    ({drained} new alert(s))");
                }

                frameDone.Set();
            }
        }) { Name = "htop", IsBackground = true };
        htopThread.Start();

        for (var frame = 0; frame < 4; frame++) {
            Console.WriteLine($"kernel frame {frame}");
            clock.Delta = 0.5f;
            kernelStage.Tick();

            foreach (var entity in kernel) {
                if (entity.Contains<Cpu>() && entity.Get<Cpu>().Load > 0.8f) {
                    alerts.TryWrite(entity.Id.Value);
                }
            }

            snapshotSystem.RunExtract();

            frameReady.Set();
            frameDone.WaitOne();
        }

        running = false;
        frameReady.Set();
        htopThread.Join(TimeSpan.FromSeconds(2));
    }

    public sealed class ChaosMonkeySystem() : SystemBase(
        Matchers.Of<Proc>())
    {
        private readonly ConcurrentStack<Entity> _toKill = [];

        public override void Execute(World world, IEntityQuery query)
        {
            var before = world.Count;
            query.ForEachOnParallel(this, static (sys, entity) => {
                if (Random.Shared.Next(4) == 0) {
                    sys._toKill.Push(entity);
                }
            });
            foreach (var entity in _toKill) {
                entity.Destroy();
            }
            _toKill.Clear();
            Console.WriteLine($"chaos monkey destroyed {before - world.Count} process(es).");
        }
    }

    private static void RunChaosMonkey(World world)
    {
        Console.WriteLine();
        Console.WriteLine("-- Scene 3: chaos monkey --");

        using var stage = SystemChain.Empty
            .Add<ChaosMonkeySystem>()
            .CreateStage(world);

        for (var i = 0; i < 2000; i++) {
            world.Create(HList.From(
                new Proc($"task-{i}"),
                new Cpu(0.1f),
                new Mem(10)));
        }

        stage.Tick();
    }

    public static void Run(World world)
    {
        RunExporters(world);
        RunLiveMonitor(world);
        RunChaosMonkey(world);
    }
}
