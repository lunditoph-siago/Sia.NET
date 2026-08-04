namespace Sia_Examples;

using Sia;

public static partial class Example1_OomKiller
{
    public class Kernel : IAddon
    {
        public float DeltaTime { get; private set; }
        public float Time { get; private set; }

        public void Tick(float deltaTime)
        {
            DeltaTime = deltaTime;
            Time += deltaTime;
        }
    }

    public readonly record struct Process(string Name);

    public partial record struct CoreAffinity([Sia] int Core);

    public partial record struct Memory(
        [Sia] int Used,
        [Sia] int Limit,
        [Sia] int LeakRate,
        [Sia] int GcRate)
    {
        public Memory() : this(0, 100, 0, 0) {}

        public readonly record struct Allocate(int Amount) : ICommand
        {
            public void Execute(World world, Entity target)
                => new View(target, world).Used += Amount;
        }
    }

    public static class Proc
    {
        public static Entity Spawn(
            World world, string name, int limit = 100, int leakRate = 0, int gcRate = 0)
            => world.Create(HList.From(
                new Process(name),
                new Memory { Limit = limit, LeakRate = leakRate, GcRate = gcRate },
                new CoreAffinity(0)));
    }

    public class MemoryLeakSystem() : SystemBase(
        Matchers.Of<Memory>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var kernel = world.GetAddon<Kernel>();
            foreach (var entity in query) {
                ref var mem = ref entity.Get<Memory>();
                if (mem.LeakRate == 0) {
                    continue;
                }
                entity.Execute(new Memory.Allocate((int)(mem.LeakRate * kernel.DeltaTime)));
            }
        }
    }

    public class GcSystem() : SystemBase(
        Matchers.Of<Memory>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var mem = ref entity.Get<Memory>();
                if (mem.GcRate == 0) {
                    continue;
                }
                if (mem.Used > 0) {
                    mem.Used = Math.Max(mem.Used - mem.GcRate, 0);
                    Console.WriteLine("gc: reclaimed memory, used=" + mem.Used);
                }
                else {
                    Console.WriteLine("gc: heap is clean, nothing to collect.");
                }
            }
        }
    }

    public class OomKillerSystem() : SystemBase(
        Matchers.Of<Memory>())
    {
        private readonly List<Entity> _dead = [];

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                if (entity.Get<Memory>().Used >= entity.Get<Memory>().Limit) {
                    _dead.Add(entity);
                }
            }
            foreach (var entity in _dead) {
                var name = entity.Contains<Process>() ? entity.Get<Process>().Name
                    : entity.Contains<Combatant>() ? new Combatant.View(entity, world).Name
                    : "?";
                entity.Destroy();
                Console.WriteLine($"oom-killer: [{name}] out of memory. Killed.");
            }
            _dead.Clear();
        }
    }

    public class MemorySystems() : SystemBase(
        SystemChain.Empty
            .Add<MemoryLeakSystem>()
            .Add<GcSystem>()
            .Add<OomKillerSystem>());

    public class AffinitySystem() : SystemBase(
        Matchers.Of<Process, Memory, CoreAffinity>(),
        EventUnion.Of<WorldEvents.Add<Memory>, CoreAffinity.SetCore>())
    {
        private const int HotCore = 7;

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                var core = entity.Get<CoreAffinity>().Core;
                if (core != HotCore) {
                    continue;
                }
                var name = entity.Get<Process>().Name;
                new Memory.View(entity, world).Used += 15;
                Console.WriteLine($"[{name}] migrated onto contended core {HotCore}; cache-thrash tax applied.");
            }
        }
    }

    public static class Signals
    {
        public sealed class Term : SingletonEvent<Term>, ICancellableEvent;

        public static void Kill(Entity process)
        {
            var name = process.Contains<Process>() ? process.Get<Process>().Name : "?";
            process.Destroy();
            Console.WriteLine($"SIGKILL delivered to [{name}]: no handler can catch this one.");
        }
    }

    public class SignalTermSystem() : SystemBase(
        Matchers.Of<Process>(),
        EventUnion.Of<Signals.Term>(),
        filter: EventUnion.Of<HOEvents.Cancel<Signals.Term>>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            Console.WriteLine("delivering SIGTERM...");
            foreach (var entity in query) {
                var name = entity.Get<Process>().Name;
                entity.Destroy();
                Console.WriteLine($"[{name}] terminated gracefully.");
            }
        }
    }

    public enum ExploitType
    {
        Overflow,
        RaceCondition,
        Deserialize,
        Injection,
    }

    public readonly record struct SignalDamage(
        string ExploitName,
        ExploitType Type,
        int BaseCost,
        int BonusCost,
        float BonusMultiplier,
        int PrivilegeSpent)
    {
        public int Total => BaseCost + BonusCost;
    }

    public interface IExploitProvider
    {
        public SignalDamage GetDamage(
            in ExploitMetadata metadata,
            Entity exploit,
            Entity attacker,
            Entity target,
            World world);
    }

    public readonly record struct ExploitMetadata(
        string Name,
        int BaseCost,
        int BonusCost,
        int PrivilegeCost,
        IExploitProvider Provider);

    public static class ZeroDay
    {
        private sealed class Provider : IExploitProvider
        {
            public static readonly Provider Instance = new();

            public SignalDamage GetDamage(
                in ExploitMetadata metadata,
                Entity exploit,
                Entity attacker,
                Entity target,
                World world)
            {
                var payload = exploit.Get<Payload>();
                var attackerView = new Combatant.View(attacker, world);
                if (attackerView.Privilege < metadata.PrivilegeCost) {
                    return new(metadata.Name, payload.Type, metadata.BaseCost, 0, 1, 0);
                }

                attackerView.Privilege -= metadata.PrivilegeCost;

                ref readonly var targetClass = ref target.Get<ProcessClass>();
                var multiplier = targetClass.Weakness == payload.Type ? 1.5f : 1f;
                return new(
                    metadata.Name,
                    payload.Type,
                    metadata.BaseCost,
                    (int)(metadata.BonusCost * multiplier),
                    multiplier,
                    metadata.PrivilegeCost);
            }
        }

        public readonly record struct Payload(ExploitType Type);

        public static readonly ExploitMetadata Metadata = new(
            Name: "0-day RCE",
            BaseCost: 8,
            BonusCost: 18,
            PrivilegeCost: 10,
            Provider: Provider.Instance);

        public static Entity Create(World world, ExploitType type)
            => world.Create(HList.From(Metadata, new Payload(type)));
    }

    public readonly record struct ProcessClass(
        string Kind,
        int BaseAttack,
        int BaseDefense,
        int AttackGrowth,
        int DefenseGrowth,
        int BasePrivilege,
        int PrivilegeGrowth,
        ExploitType? Weakness)
    {
        public int Attack(int level) => BaseAttack + level * AttackGrowth;
        public int Defense(int level) => BaseDefense + level * DefenseGrowth;
        public int MaxPrivilege(int level) => BasePrivilege + level * PrivilegeGrowth;

        public Combatant CreateCombatant(string name, int level)
            => new(name, MaxPrivilege(level), level);
    }

    public partial record struct Combatant(
        [Sia] string Name,
        [Sia] int Privilege,
        [Sia] int Level = 0,
        [Sia] Entity? Exploit = null)
    {
        public readonly record struct Signal(Entity Attacker) : ICommand
        {
            public void Execute(World world, Entity target)
            {
                if (!IsCombatant(Attacker) || !IsCombatant(target)) {
                    Console.WriteLine("signal ignored: sender or target already reaped.");
                    return;
                }

                ref readonly var attackerClass = ref Attacker.Get<ProcessClass>();
                ref readonly var targetClass = ref target.Get<ProcessClass>();
                var attacker = new View(Attacker, world);
                var defender = new View(target, world);

                var baseCost = attackerClass.Attack(attacker.Level);
                SignalDamage? exploitDamage = null;
                if (attacker.Exploit is Entity { IsValid: true } exploit
                        && exploit.Contains<ExploitMetadata>()) {
                    ref readonly var metadata = ref exploit.Get<ExploitMetadata>();
                    exploitDamage = metadata.Provider.GetDamage(metadata, exploit, Attacker, target, world);
                }

                var rawCost = baseCost + (exploitDamage?.Total ?? 0);
                var defense = targetClass.Defense(defender.Level);
                var actual = Math.Max(0, rawCost - defense);

                Console.WriteLine(
                    $"{attacker.Name} signals {defender.Name}: "
                    + $"{rawCost} raw - {defense} defense = {actual} forced allocation");
                if (exploitDamage is { } damage) {
                    var bonus = damage.BonusCost > 0
                        ? $" + {damage.BonusCost} {damage.Type} bonus (x{damage.BonusMultiplier:0.#})"
                        : " + no bonus (insufficient privilege)";
                    Console.WriteLine(
                        $"  {damage.ExploitName}: {damage.BaseCost} base"
                        + bonus
                        + $", {damage.PrivilegeSpent} privilege spent");
                }

                target.Execute(new Memory.Allocate(actual));

                if (target.IsValid) {
                    ref readonly var mem = ref target.Get<Memory>();
                    Console.WriteLine(
                        $"  {defender.Name} memory: {mem.Used}/{mem.Limit}; "
                        + $"{attacker.Name} privilege: {attacker.Privilege}");
                }
            }

            private static bool IsCombatant(Entity entity)
                => entity.IsValid
                    && entity.Contains<Combatant>()
                    && entity.Contains<ProcessClass>();
        }
    }

    public static class WebProcess
    {
        public static readonly ProcessClass Metadata = new(
            Kind: "nginx",
            BaseAttack: 10,
            BaseDefense: 6,
            AttackGrowth: 5,
            DefenseGrowth: 4,
            BasePrivilege: 50,
            PrivilegeGrowth: 7,
            Weakness: ExploitType.Deserialize);

        public static Entity Create(World world, string name, int level = 0)
            => world.Create(HList.From(
                Metadata,
                Metadata.CreateCombatant(name, level),
                new Memory { Limit = 200 }));
    }

    public static class CronDaemon
    {
        public static readonly ProcessClass Metadata = new(
            Kind: "cron",
            BaseAttack: 20,
            BaseDefense: 3,
            AttackGrowth: 4,
            DefenseGrowth: 2,
            BasePrivilege: 0,
            PrivilegeGrowth: 0,
            Weakness: ExploitType.RaceCondition);

        public static Entity Create(World world, string name, int level = 0)
            => world.Create(HList.From(
                Metadata,
                Metadata.CreateCombatant(name, level),
                new Memory { Limit = 200 }));
    }

    public class KernelLogSystems() : SystemBase(
        SystemChain.Empty
            .Add((ref Memory mem) => Console.WriteLine($"dmesg: page fault handled, used={mem.Used}/{mem.Limit}"),
                trigger: EventUnion.Of<Memory.Allocate>())
            .Add((ref CoreAffinity aff) => Console.WriteLine("dmesg: task migrated to core " + aff.Core),
                trigger: EventUnion.Of<CoreAffinity.SetCore>()));

    public class SchedulerSystem(string queue) : SystemBase(
        Matchers.Any)
    {
        private readonly string _queue = queue;

        public override void Execute(World world, IEntityQuery query)
            => Console.WriteLine($"[sched:{_queue}] tick — {query.Count} runnable process(es)");
    }

    public static void Run(World world)
    {
        var kernel = world.AcquireAddon<Kernel>();

        using var stage = SystemChain.Empty
            .Add<MemorySystems>()
            .Add<AffinitySystem>()
            .Add<SignalTermSystem>()
            .Add<KernelLogSystems>()
            .Add<SchedulerSystem>(() => new("foreground"))
            .Add<SchedulerSystem>(() => new("background"))
            .CreateStage(world);

        Console.WriteLine("-- Scene 1: memory leak vs GC vs the OOM killer --");

        Proc.Spawn(world, "init");
        var leaky = Proc.Spawn(world, "leaky-daemon", limit: 60, leakRate: 12, gcRate: 4);

        for (var tick = 1; leaky.IsValid && tick <= 8; ++tick) {
            Console.WriteLine($"tick {tick}");
            kernel.Tick(1f);
            stage.Tick();
        }

        Console.WriteLine();
        Console.WriteLine("-- Scene 2: scheduling onto a contended core --");

        var web = Proc.Spawn(world, "web-worker", limit: 200);
        new CoreAffinity.View(web, world) { Core = 7 };
        stage.Tick();

        Console.WriteLine();
        Console.WriteLine("-- Scene 3: SIGTERM vs SIGKILL --");

        var stubborn = Proc.Spawn(world, "stubborn-service", limit: 200);

        world.Send(stubborn, Signals.Term.Instance);
        world.Send(stubborn, HOEvents.Cancel<Signals.Term>.Instance);
        stage.Tick();
        Console.WriteLine(stubborn.IsValid
            ? "stubborn-service trapped SIGTERM and lives on."
            : "stubborn-service exited.");

        Signals.Kill(stubborn);
        Console.WriteLine(stubborn.IsValid ? "still alive?!" : "gone for good.");

        Console.WriteLine();
        Console.WriteLine("-- Scene 4: nginx and cron fight over /var/lock --");

        var nginx = WebProcess.Create(world, "nginx", level: 3);
        var cron = CronDaemon.Create(world, "cron", level: 2);
        var exploit = ZeroDay.Create(world, ExploitType.RaceCondition);

        new Combatant.View(nginx, world) { Exploit = exploit };
        PrintCombatant(nginx, world);
        PrintCombatant(cron, world);

        for (var round = 1; nginx.IsValid && cron.IsValid; ++round) {
            Console.WriteLine($"-- round {round} --");
            cron.Execute(new Combatant.Signal(nginx));
            stage.Tick();
            if (!cron.IsValid) {
                break;
            }

            nginx.Execute(new Combatant.Signal(cron));
            stage.Tick();
        }

        Console.WriteLine("-- result --");
        if (nginx.IsValid) {
            PrintCombatant(nginx, world);
        }
        if (cron.IsValid) {
            PrintCombatant(cron, world);
        }
    }

    private static void PrintCombatant(Entity entity, World world)
    {
        ref readonly var cls = ref entity.Get<ProcessClass>();
        var combatant = new Combatant.View(entity, world);
        ref readonly var mem = ref entity.Get<Memory>();
        Console.WriteLine(
            $"{combatant.Name} [{cls.Kind}, Lv.{combatant.Level}] "
            + $"privilege {combatant.Privilege}, memory {mem.Used}/{mem.Limit}, "
            + $"atk {cls.Attack(combatant.Level)}, def {cls.Defense(combatant.Level)}");
    }
}
