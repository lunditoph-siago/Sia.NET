namespace Sia.Tests.Systems;

public class SchedulingTests
{
    private static readonly SystemSetLabel TestSet = new(nameof(TestSet));

    public class OrderedFirstSystem : SystemBase;
    public class OrderedSecondSystem : SystemBase;
    public class SetMemberSystem : SystemBase;
    public class AfterSetSystem : SystemBase;

    [SiaSystem]
    [SiaBefore<GeneratedSecondSystem>]
    public class GeneratedFirstSystem : SystemBase;

    [SiaSystem]
    public class GeneratedSecondSystem : SystemBase;

    public readonly struct AdvancedSimulationSet : ISystemSet;
    public readonly struct AdvancedPresentationSet : ISystemSet;

    [SiaSystem]
    public class AdvancedInputBridgeSystem() : SystemBase(Matchers.Any)
    {
        public override void Execute(World world, IEntityQuery query)
            => InitOrder.Add(nameof(AdvancedInputBridgeSystem));
    }

    [SiaSystem]
    [SiaInSet<AdvancedSimulationSet>]
    [SiaAfter<AdvancedInputBridgeSystem>]
    public class AdvancedPhysicsSystem() : SystemBase(Matchers.Any)
    {
        public override void Execute(World world, IEntityQuery query)
            => InitOrder.Add(nameof(AdvancedPhysicsSystem));
    }

    [SiaSystem]
    [SiaInSet<AdvancedPresentationSet>]
    [SiaAfterSet<AdvancedSimulationSet>]
    public class AdvancedRenderPrepSystem() : SystemBase(Matchers.Any)
    {
        public override void Execute(World world, IEntityQuery query)
            => InitOrder.Add(nameof(AdvancedRenderPrepSystem));
    }

    public class StartupInitSystem : SystemBase
    {
        public override void Initialize(World world) => InitOrder.Add(nameof(StartupInitSystem));
    }

    public class UpdateInitSystem : SystemBase
    {
        public override void Initialize(World world) => InitOrder.Add(nameof(UpdateInitSystem));
    }

    private sealed class NamedInitSystem(string name) : SystemBase
    {
        public override void Initialize(World world) => InitOrder.Add(name);
    }

    private sealed class TestScheduleEntry(string name) : IScheduleEntry
    {
        public void Tick() => InitOrder.Add(name);
    }

    private sealed class OneShotScheduleSource(Scheduler scheduler) : IScheduleSource
    {
        private bool _initialized;
        private bool _updateAdded;

        public void OnBeginTick()
        {
            if (_initialized) {
                return;
            }
            _initialized = true;
            scheduler.ConfigureSchedule(
                CoreSchedules.Update,
                static schedule => schedule.After(CoreSchedules.First));
            scheduler.AddEntry(CoreSchedules.First, new TestScheduleEntry("first"));
        }

        public void OnBeforeSchedule(ScheduleLabel label)
        {
            if (label == CoreSchedules.Update && !_updateAdded) {
                _updateAdded = true;
                scheduler.AddEntry(label, new TestScheduleEntry("update"));
            }
        }
    }

    private static readonly List<string> InitOrder = [];

    [Fact]
    public void Schedule_IsImmutable()
    {
        var original = new Schedule(CoreSchedules.Update);
        var configured = original
            .Add<OrderedFirstSystem>()
            .Before(CoreSchedules.Last);

        Assert.Empty(original.Chain.Entries);
        Assert.Empty(original.RunsBefore);
        Assert.Single(configured.Chain.Entries);
        Assert.Contains(CoreSchedules.Last, configured.RunsBefore);
    }

    [Fact]
    public void Plan_OrdersSystemsByTypeDependency()
    {
        var chain = SystemChain.Empty
            .Add<OrderedSecondSystem>()
            .Add<OrderedFirstSystem>()
            .Configure<OrderedFirstSystem>(static descriptor => descriptor.Before<OrderedSecondSystem>());

        var systems = chain.Plan().Entries.Select(entry => entry.Id.Type).ToArray();

        Assert.Equal([typeof(OrderedFirstSystem), typeof(OrderedSecondSystem)], systems);
    }

    [Fact]
    public void Planner_IsStableAndDoesNotInstantiateSystems()
    {
        var creations = 0;
        var first = SystemId.Func("first");
        var second = SystemId.Func("second");
        var third = SystemId.Func("third");
        var plan = SystemChain.Empty
            .Add(first, Create)
            .Add(second, Create)
            .Add(third, Create)
            .Configure(second, descriptor => descriptor.After(third))
            .Plan();

        Assert.Equal([first, third, second], plan.Entries.Select(entry => entry.Id));
        Assert.Equal(0, creations);

        ISystem Create()
        {
            creations++;
            return new NamedInitSystem("created");
        }
    }

    [Fact]
    public void Planner_ReportsOnlyExactNamedCycle()
    {
        var upstream = SystemId.Func("upstream");
        var first = SystemId.Func("cycle.first");
        var second = SystemId.Func("cycle.second");
        var chain = SystemChain.Empty
            .Add(upstream, static () => new NamedInitSystem("upstream"))
            .Add(first, static () => new NamedInitSystem("first"))
            .Add(second, static () => new NamedInitSystem("second"))
            .Configure(upstream, descriptor => descriptor.Before(first))
            .Configure(first, descriptor => descriptor.Before(second))
            .Configure(second, descriptor => descriptor.Before(first));

        var exception = Assert.Throws<SystemCycleException>(() => chain.Plan());

        Assert.Equal([first, second, first], exception.Cycle.ToArray());
        Assert.DoesNotContain(upstream, exception.Cycle);
    }

    [Fact]
    public void Plan_OrdersSystemsBySetDependency()
    {
        var chain = SystemChain.Empty
            .Add<AfterSetSystem>()
            .Add<SetMemberSystem>()
            .Configure<SetMemberSystem>(descriptor => descriptor.InSet(TestSet))
            .Configure<AfterSetSystem>(descriptor => descriptor.After(TestSet));

        var systems = chain.Plan().Entries.Select(entry => entry.Id.Type).ToArray();

        Assert.Equal([typeof(SetMemberSystem), typeof(AfterSetSystem)], systems);
    }

    [Fact]
    public void Plan_UsesGeneratedSystemDescriptors()
    {
        var chain = SystemChain.Empty
            .Add<GeneratedSecondSystem>()
            .Add<GeneratedFirstSystem>();

        var systems = chain.Plan().Entries.Select(entry => entry.Id.Type).ToArray();

        Assert.Equal([typeof(GeneratedFirstSystem), typeof(GeneratedSecondSystem)], systems);
    }

    [Fact]
    public void Plan_OrdersNamedFunctionSystems()
    {
        InitOrder.Clear();
        using var world = new World();
        const string firstName = "first";
        const string secondName = "second";
        var first = SystemId.Func(firstName);
        var second = SystemId.Func(secondName);
        var stage = SystemChain.Empty
            .Add(second, () => new NamedInitSystem(secondName))
            .Add(first, () => new NamedInitSystem(firstName))
            .Configure(second, descriptor => descriptor.After(first))
            .CreateStage(world);

        stage.Dispose();

        Assert.Equal([firstName, secondName], InitOrder);
    }

    [Fact]
    public void Plan_ComposesGeneratedTypesSetsAndNamedFunctions()
    {
        InitOrder.Clear();
        using var world = new World();
        var collectInput = SystemId.Func("advanced.collect-input");
        var commitInput = SystemId.Func("advanced.commit-input");
        var telemetry = SystemId.Func("advanced.telemetry");

        using var stage = SystemChain.Empty
            .Add<AdvancedRenderPrepSystem>()
            .Add(commitInput, () => new FSystem((_, _, _) => InitOrder.Add(nameof(commitInput)), Matchers.Any))
            .Add<AdvancedPhysicsSystem>()
            .Add(telemetry, () => new FSystem((_, _, _) => InitOrder.Add(nameof(telemetry)), Matchers.Any))
            .Add(collectInput, () => new FSystem((_, _, _) => InitOrder.Add(nameof(collectInput)), Matchers.Any))
            .Add<AdvancedInputBridgeSystem>()
            .Configure(commitInput, descriptor => descriptor
                .After(collectInput)
                .Before<AdvancedInputBridgeSystem>())
            .Configure(telemetry, descriptor => descriptor
                .AfterSet<AdvancedPresentationSet>())
            .CreateStage(world);

        stage.Tick();

        Assert.Equal(
            [
                nameof(collectInput),
                nameof(commitInput),
                nameof(AdvancedInputBridgeSystem),
                nameof(AdvancedPhysicsSystem),
                nameof(AdvancedRenderPrepSystem),
                nameof(telemetry)
            ],
            InitOrder);
    }

    [Fact]
    public void SystemId_TypeIdentityIgnoresNameAndUsesTypeHash()
    {
        var genericId = SystemId.For<AdvancedPhysicsSystem>();
        var runtimeId = SystemId.ForType(typeof(AdvancedPhysicsSystem));
        var sameNameFunctionId = SystemId.Func(typeof(AdvancedPhysicsSystem).FullName!);

        Assert.Equal(genericId, runtimeId);
        Assert.Equal(genericId.GetHashCode(), runtimeId.GetHashCode());
        Assert.NotEqual(genericId, sameNameFunctionId);
    }

    [Fact]
    public void Scheduler_OrdersSchedulesByDependency()
    {
        InitOrder.Clear();
        using var world = new World();
        var scheduler = world.AddAddon<Scheduler>()
            .ConfigureSchedule(CoreSchedules.Update, static schedule =>
                schedule.Add<UpdateInitSystem>().After(CoreSchedules.Startup))
            .ConfigureSchedule(CoreSchedules.Startup, static schedule =>
                schedule.Add<StartupInitSystem>());

        scheduler.Tick();

        Assert.Equal(
            [nameof(StartupInitSystem), nameof(UpdateInitSystem)],
            InitOrder);
    }

    [Fact]
    public void Scheduler_AppliesSourceContributionsBeforeFirstPlan()
    {
        InitOrder.Clear();
        using var world = new World();
        var scheduler = world.AddAddon<Scheduler>();
        scheduler.AddSource(new OneShotScheduleSource(scheduler));

        scheduler.Tick();

        Assert.Equal(["first", "update"], InitOrder);
    }

    [Fact]
    public void Scheduler_ReplansChangesAndRunsManualSchedulesOnlyExplicitly()
    {
        InitOrder.Clear();
        using var world = new World();
        var manual = new ScheduleLabel("manual");
        var scheduler = world.AddAddon<Scheduler>();
        scheduler.AddEntry(CoreSchedules.First, new TestScheduleEntry("first"));
        scheduler.AddEntry(CoreSchedules.Update, new TestScheduleEntry("update"));
        scheduler.AddEntry(manual, new TestScheduleEntry("manual"));
        scheduler.ConfigureSchedule(manual, static schedule => schedule.AsManual());

        scheduler.Tick();
        scheduler.ConfigureSchedule(
            CoreSchedules.First,
            static schedule => schedule.After(CoreSchedules.Update));
        scheduler.Tick();
        scheduler.TickSchedule(manual);

        Assert.Equal(
            ["first", "update", "update", "first", "manual"],
            InitOrder);
    }
}
