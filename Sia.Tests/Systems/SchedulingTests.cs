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
    public void SystemGraph_OrdersSystemsByTypeDependency()
    {
        var chain = SystemChain.Empty
            .Add<OrderedSecondSystem>()
            .Add<OrderedFirstSystem>()
            .With<OrderedFirstSystem>(static descriptor => descriptor.Before<OrderedSecondSystem>());

        var systems = chain.ToGraph().BuildSortedSystems().Select(system => system.GetType()).ToArray();

        Assert.Equal([typeof(OrderedFirstSystem), typeof(OrderedSecondSystem)], systems);
    }

    [Fact]
    public void SystemGraph_OrdersSystemsBySetDependency()
    {
        var chain = SystemChain.Empty
            .Add<AfterSetSystem>()
            .Add<SetMemberSystem>()
            .With<SetMemberSystem>(descriptor => descriptor.InSet(TestSet))
            .With<AfterSetSystem>(descriptor => descriptor.After(TestSet));

        var systems = chain.ToGraph().BuildSortedSystems().Select(system => system.GetType()).ToArray();

        Assert.Equal([typeof(SetMemberSystem), typeof(AfterSetSystem)], systems);
    }

    [Fact]
    public void SystemGraph_UsesGeneratedSystemDescriptors()
    {
        var chain = SystemChain.Empty
            .Add<GeneratedSecondSystem>()
            .Add<GeneratedFirstSystem>();

        var systems = chain.ToGraph().BuildSortedSystems().Select(system => system.GetType()).ToArray();

        Assert.Equal([typeof(GeneratedFirstSystem), typeof(GeneratedSecondSystem)], systems);
    }

    [Fact]
    public void SystemGraph_OrdersNamedFunctionSystems()
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
            .With(second, descriptor => descriptor.After(first))
            .CreateSortedStage(world);

        stage.Dispose();

        Assert.Equal([firstName, secondName], InitOrder);
    }

    [Fact]
    public void SystemGraph_ComposesGeneratedTypesSetsAndNamedFunctions()
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
            .With(commitInput, descriptor => descriptor
                .After(collectInput)
                .Before<AdvancedInputBridgeSystem>())
            .With(telemetry, descriptor => descriptor
                .AfterSet<AdvancedPresentationSet>())
            .CreateSortedStage(world);

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

        scheduler.Build();

        Assert.Equal(
            [nameof(StartupInitSystem), nameof(UpdateInitSystem)],
            InitOrder);
    }
}
