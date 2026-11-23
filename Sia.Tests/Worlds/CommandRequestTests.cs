namespace Sia.Tests.Worlds;

public class CommandRequestTests
{
    private readonly record struct AddAmount(int Amount) : ICommand
    {
        public void Execute(World world, Entity target)
            => target.Get<int>() += Amount;
    }

    private readonly record struct MultiplyBy(int Factor) : ICommand
    {
        public void Execute(World world, Entity target)
            => target.Get<int>() *= Factor;
    }

    private struct ReplayCommand : ICommand
    {
        public int Runs { get; private set; }

        public void Execute(World world, Entity target)
            => target.Get<int>() += ++Runs;
    }

    private class BaseCommand(int amount) : ICommand
    {
        public void Execute(World world, Entity target)
            => target.Get<int>() += amount;
    }

    private sealed class DerivedCommand(int amount) : BaseCommand(amount) {}

    private sealed class GlobalCommandObserver : IEventListener<Entity>
    {
        public List<(Type EventType, Entity Target, ICommand Command, int Value, int? Runs)> Calls { get; } = [];

        public bool OnEvent<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (e is ICommand command) {
                var runs = command is ReplayCommand replay ? (int?)replay.Runs : null;
                Calls.Add((typeof(TEvent), target, command, target.Get<int>(), runs));
            }
            return false;
        }
    }

    [Fact]
    public void TypedRequest_RejectsNullAtConstruction()
    {
        Assert.Throws<ArgumentNullException>("command",
            () => CommandRequest.Create<BaseCommand>(default, null!));
    }

    [Fact]
    public void DirectExecute_NotifiesConcreteListenerAfterApplyingCommand()
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var observations = Observe<AddAmount>(world);
        var command = new AddAmount(7);

        world.Execute(target, command);

        Assert.Equal(17, target.Get<int>());
        Assert.Equal((target, command, 17), Assert.Single(observations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredCommands_PublishOnlyTheirDeclaredTypeAfterExecution(bool viaActor)
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var concreteEvents = Observe<AddAmount>(world);
        var interfaceEvents = Observe<ICommand>(world);
        var concrete = new AddAmount(7);
        ICommand erased = new AddAmount(5);
        var typedRequest = CommandRequest.Create(target, concrete);
        var erasedRequest = new CommandRequest(target, erased);

        using var actor = viaActor ? new WorldActor(world) : null;
        if (actor is null) {
            world.Commands.Enqueue(typedRequest);
            world.Commands.Enqueue(erasedRequest);
        }
        else {
            world.Post(typedRequest);
            actor.Post(erasedRequest);
        }

        Assert.Empty(concreteEvents);
        Assert.Empty(interfaceEvents);
        Assert.Equal(10, target.Get<int>());

        if (actor is null) {
            Assert.Equal(2, world.Commands.Apply(world));
        }
        else {
            actor.Complete();
            actor.Run();
            Assert.Null(actor.Failure);
        }

        Assert.Equal(22, target.Get<int>());
        Assert.Equal((target, concrete, 17), Assert.Single(concreteEvents));
        var observed = Assert.Single(interfaceEvents);
        Assert.Equal((target, erased, 22), observed);
        Assert.Same(erased, observed.Command);
    }

    [Fact]
    public void QueueApply_ReplacingCommandUsesInterfaceDispatchWithoutChangingTheOriginal()
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var additions = Observe<AddAmount>(world);
        var multiplications = Observe<MultiplyBy>(world);
        var interfaceEvents = Observe<ICommand>(world);
        var original = new AddAmount(7);
        ICommand replacement = new MultiplyBy(3);
        var request = CommandRequest.Create(target, original);

        world.Commands.Enqueue(request);
        world.Commands.Enqueue(request with { Command = replacement });

        Assert.Equal(2, world.Commands.Apply(world));
        Assert.Equal(51, target.Get<int>());
        Assert.Equal((target, original, 17), Assert.Single(additions));
        Assert.Empty(multiplications);
        var observed = Assert.Single(interfaceEvents);
        Assert.Equal((target, replacement, 51), observed);
        Assert.Same(replacement, observed.Command);
    }

    [Fact]
    public void ErasedRequest_ReplayMutatesTheOriginalBoxAndNotifiesGlobalObserver()
    {
        using var world = new World();
        var target = world.Create(HList.From(0));
        var observer = new GlobalCommandObserver();
        world.Dispatcher.Listen(observer);
        ICommand command = new ReplayCommand();
        var request = new CommandRequest(target, command);

        world.Commands.Enqueue(request);
        world.Commands.Enqueue(request);

        Assert.Equal(2, world.Commands.Apply(world));
        Assert.Equal(3, target.Get<int>());
        Assert.Same(command, request.Command);
        Assert.Equal(2, Assert.IsType<ReplayCommand>(command).Runs);
        Assert.All(observer.Calls, call => Assert.Same(command, call.Command));
        Assert.Equal(new[] {
            (typeof(ICommand), target, (int?)1, 1),
            (typeof(ICommand), target, (int?)2, 3)
        }, observer.Calls.Select(call => (call.EventType, call.Target, call.Runs, call.Value)));
    }

    [Fact]
    public void TypedRequest_ReplaysMutableStructWithTheSameValueSemanticsAsDirectExecute()
    {
        using var world = new World();
        var direct = world.Create(HList.From(0));
        var queued = world.Create(HList.From(0));
        var observations = Observe<ReplayCommand>(world);
        var command = new ReplayCommand();
        var request = CommandRequest.Create(queued, command);

        world.Execute(direct, command);
        world.Execute(direct, command);
        world.Commands.Enqueue(request);
        world.Commands.Enqueue(request);

        Assert.Equal(2, world.Commands.Apply(world));
        Assert.Equal(2, direct.Get<int>());
        Assert.Equal(direct.Get<int>(), queued.Get<int>());
        Assert.Equal(0, Assert.IsType<ReplayCommand>(request.Command).Runs);
        Assert.Equal(new[] {
            (direct, command, 1),
            (direct, command, 2),
            (queued, command, 1),
            (queued, command, 2)
        }, observations);
    }

    [Fact]
    public void TypedRequest_MatchesDirectDispatchTypeAndPreservesItWhenRetargeted()
    {
        using var world = new World();
        var first = world.Create(HList.From(0));
        var second = world.Create(HList.From(0));
        var observer = new GlobalCommandObserver();
        world.Dispatcher.Listen(observer);
        var command = new DerivedCommand(3);
        ICommand erased = command;

        world.Execute<BaseCommand>(first, command);
        world.Execute<DerivedCommand>(first, command);
        world.Execute(first, erased);

        var baseRequest = CommandRequest.Create<BaseCommand>(first, command);
        var derivedRequest = CommandRequest.Create<DerivedCommand>(first, command);
        var interfaceRequest = CommandRequest.Create(first, erased);
        (baseRequest with { Target = second }).Apply(world);
        derivedRequest.Apply(world);
        interfaceRequest.Apply(world);

        Assert.Equal(new[] {
            (typeof(BaseCommand), first, 3),
            (typeof(DerivedCommand), first, 6),
            (typeof(ICommand), first, 9),
            (typeof(BaseCommand), second, 3),
            (typeof(DerivedCommand), first, 12),
            (typeof(ICommand), first, 15)
        }, observer.Calls.Select(call => (call.EventType, call.Target, call.Value)));
        Assert.All(observer.Calls, call => Assert.Same(command, call.Command));

        var erasedRequest = new CommandRequest(first, erased);
        Assert.NotEqual(baseRequest, erasedRequest);
        Assert.NotEqual(derivedRequest, erasedRequest);
        Assert.Equal(interfaceRequest, erasedRequest);
        Assert.Equal(interfaceRequest.GetHashCode(), erasedRequest.GetHashCode());
        var requests = new HashSet<CommandRequest> { baseRequest };
        Assert.True(requests.Add(derivedRequest));
        Assert.True(requests.Add(interfaceRequest));
        Assert.False(requests.Add(erasedRequest));
    }

    private static List<(Entity Target, TCommand Command, int Value)> Observe<TCommand>(World world)
        where TCommand : ICommand
    {
        var observations = new List<(Entity, TCommand, int)>();
        world.Dispatcher.Listen<TCommand>((Entity target, in TCommand command) => {
            observations.Add((target, command, target.Get<int>()));
            return false;
        });
        return observations;
    }
}
