namespace Sia.Tests.Systems;

public class SystemStageLifecycleTests
{
    private readonly record struct ProbeComponent;
    private readonly record struct ProbeEvent : IEvent;
    private record struct ProbeValue(int Value);

    private readonly record struct SetProbeValue(int Value) : ICommand
    {
        public void Execute(World world, Entity target)
            => target.Get<ProbeValue>().Value = Value;
    }

    private sealed class LifecycleSystem(
        string name,
        List<string> calls,
        Exception? initializationError = null,
        Exception? uninitializationError = null,
        SystemChain? children = null) : SystemBase(children)
    {
        public override void Initialize(World world)
        {
            calls.Add($"{name}.initialize");
            if (initializationError != null) {
                throw initializationError;
            }
        }

        public override void Uninitialize(World world)
        {
            calls.Add($"{name}.uninitialize");
            if (uninitializationError != null) {
                throw uninitializationError;
            }
        }
    }

    private sealed class ThrowOnceReactiveSystem() : SystemBase(
        Matchers.Any,
        EventUnion.Of<ProbeEvent>())
    {
        public int ExecutionCount { get; private set; }

        public override void Execute(WorldContext context, IEntityQuery query)
        {
            ExecutionCount++;
            if (ExecutionCount == 1) {
                throw new InvalidOperationException("execute");
            }
        }
    }

    private sealed class CountingReactiveSystem() : SystemBase(
        Matchers.Any,
        EventUnion.Of<ProbeEvent>())
    {
        public int ExecutionCount { get; private set; }

        public override void Execute(WorldContext context, IEntityQuery query)
            => ExecutionCount++;
    }

    private sealed class AddedReactiveSystem(IEntityMatcher matcher)
        : SystemBase(matcher, EventUnion.Of<WorldEvents.Add>());

    private sealed class CountingEventUnion : IEventUnion
    {
        public ITypeUnion EventTypes { get; } = new TypeUnion<ProbeEvent>();
        public int HandleCount { get; private set; }

        public void Handle(IGenericTypeHandler<IEvent> handler)
        {
            HandleCount++;
            handler.Handle<ProbeEvent>();
        }
    }

    private sealed class CountingUnionSystem(CountingEventUnion trigger)
        : SystemBase(Matchers.Of<ProbeComponent>(), trigger)
    {
        public int ObservedCount { get; private set; }

        public override void Execute(WorldContext context, IEntityQuery query)
            => ObservedCount += query.Count;
    }

    [Fact]
    public void InitializationFailureRollsBackSystemsAndChildrenInReverseOrder()
    {
        var calls = new List<string>();
        var initializationError = new InvalidOperationException("initialize");
        var child = new LifecycleSystem("child", calls);
        var parent = new LifecycleSystem(
            "parent",
            calls,
            children: SystemChain.Empty.Add(
                SystemId.Func("child"), () => child));
        var failing = new LifecycleSystem(
            "failing", calls, initializationError);
        using var world = new World();
        var chain = SystemChain.Empty
            .Add(SystemId.Func("parent"), () => parent)
            .Add(SystemId.Func("failing"), () => failing);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => chain.CreateStage(world));

        Assert.Same(initializationError, thrown);
        Assert.Equal(
            [
                "child.initialize",
                "parent.initialize",
                "failing.initialize",
                "parent.uninitialize",
                "child.uninitialize"
            ],
            calls);
    }

    [Fact]
    public void DisposeRunsEveryCleanupInReverseAndAggregatesFailures()
    {
        var calls = new List<string>();
        var firstError = new InvalidOperationException("first");
        var secondError = new ArgumentException("second");
        var first = new LifecycleSystem(
            "first", calls, uninitializationError: firstError);
        var second = new LifecycleSystem(
            "second", calls, uninitializationError: secondError);
        var third = new LifecycleSystem("third", calls);
        using var world = new World();
        var stage = SystemChain.Empty
            .Add(SystemId.Func("first"), () => first)
            .Add(SystemId.Func("second"), () => second)
            .Add(SystemId.Func("third"), () => third)
            .CreateStage(world);
        calls.Clear();

        var thrown = Assert.Throws<AggregateException>(stage.Dispose);

        Assert.Equal(
            ["third.uninitialize", "second.uninitialize", "first.uninitialize"],
            calls);
        Assert.Equal([secondError, firstError], thrown.InnerExceptions);
        Assert.True(stage.IsDisposed);
        stage.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stage.Tick());
    }

    [Fact]
    public void ExecuteFailureRestoresReactiveCollectionForTheNextTick()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new ProbeComponent()));
        var system = new ThrowOnceReactiveSystem();
        using var stage = SystemChain.Empty
            .Add(SystemId.Func("reactive"), () => system)
            .CreateStage(world);

        world.Send(entity, new ProbeEvent());
        Assert.Throws<InvalidOperationException>(() => stage.Tick());

        world.Send(entity, new ProbeEvent());
        stage.Tick();

        Assert.Equal(2, system.ExecutionCount);
    }

    [Fact]
    public void ReleasedEntityIsUncollectedAfterItsSlotChanges()
    {
        using var world = new World();
        var first = world.Create(HList.From(new ProbeComponent()));
        var target = world.Create(HList.From(new ProbeComponent()));
        var system = new CountingReactiveSystem();
        using var stage = SystemChain.Empty
            .Add(SystemId.Func("reactive"), () => system)
            .CreateStage(world);

        world.Send(target, new ProbeEvent());
        first.Destroy();
        target.Destroy();
        stage.Tick();

        Assert.Equal(0, system.ExecutionCount);
    }

    [Fact]
    public void ReactiveQueryRegistersOncePerEventTypeAndRoutesByHost()
    {
        using var world = new World();
        var first = world.Create(HList.From(new ProbeComponent()));
        var second = world.Create(HList.From(new ProbeComponent()));
        var trigger = new CountingEventUnion();
        var system = new CountingUnionSystem(trigger);
        using var stage = SystemChain.Empty
            .Add(SystemId.Func("reactive"), () => system)
            .CreateStage(world);

        world.Send(first, new ProbeEvent());
        world.Send(second, new ProbeEvent());
        stage.Tick();

        Assert.Equal(1, trigger.HandleCount);
        Assert.Equal(2, system.ObservedCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ChildStageCommandsBecomeVisibleToSiblingsOnlyAfterTheRootTick(
        bool parentHasMatcher, bool viaScheduler)
    {
        using var world = new World();
        using var cancellation = new CancellationTokenSource();
        var expectedContext = new WorldContext(world, cancellation.Token);
        var target = world.Create(HList.From(new ProbeValue(0)));
        var seen = new List<int>();

        var child = new FSystem(
            (context, _, _, _) => {
                Assert.Equal(expectedContext, context);
                context.World.Commands.Enqueue(CommandRequest.Create(target, new SetProbeValue(1)));
            },
            Matchers.Any);
        var parent = new LifecycleSystem("parent", [], children: SystemChain.Empty.Add(() => child)) {
            Matcher = parentHasMatcher ? Matchers.Any : null,
        };
        var sibling = new FSystem(
            (context, _, _, _) => {
                Assert.Equal(expectedContext, context);
                seen.Add(target.Get<ProbeValue>().Value);
            },
            Matchers.Any);
        using var stage = SystemChain.Empty
            .Add(() => parent)
            .Add(() => sibling)
            .CreateStage(world);

        if (viaScheduler) {
            var scheduler = world.AcquireAddon<Scheduler>();
            using var registration = scheduler.RegisterEntry(new ScheduleLabel("stage-probe"), stage);
            scheduler.Tick(cancellation.Token);
        }
        else {
            stage.Tick(cancellation.Token);
        }

        Assert.Equal([0], seen);
        Assert.Equal(1, target.Get<ProbeValue>().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedStageIgnoresPendingHostAddedNotification(bool observerThrows)
    {
        using var world = new World();
        var matcher = Matchers.Of<ProbeComponent>();
        using var query = world.Query(matcher);
        var error = new InvalidOperationException("host observer");
        SystemStage? stage = null;
        query.OnEntityHostAdded += _ => {
            stage!.Dispose();
            if (observerThrows) {
                throw error;
            }
        };

        using var createdStage = SystemChain.Empty
            .Add(() => new AddedReactiveSystem(matcher))
            .CreateStage(world);
        stage = createdStage;
        var collected = Assert.IsAssignableFrom<IEntityQuery>(Assert.Single(stage.Entries).Disposable);
        IReactiveEntityHost? observedHost = null;
        query.OnEntityHostAdded += host => observedHost = host;

        if (observerThrows) {
            var thrown = Assert.Throws<InvalidOperationException>(
                () => world.GetArrayHost<HList<ProbeComponent, EmptyHList>>());
            Assert.Same(error, thrown);
        }
        else {
            world.GetArrayHost<HList<ProbeComponent, EmptyHList>>();
        }

        world.Create(HList.From(new ProbeComponent()));

        Assert.True(stage.IsDisposed);
        Assert.Same(Assert.Single(query.Hosts), observedHost);
        Assert.Equal(1, query.Count);
        Assert.Empty(collected.Hosts);
        Assert.Equal(0, collected.Count);
    }
}
