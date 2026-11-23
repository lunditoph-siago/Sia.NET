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
        public List<(Type EventType, Entity Target, ICommand Command, int Value)> Calls { get; } = [];

        public bool OnEvent<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (e is ICommand command) {
                Calls.Add((typeof(TEvent), target, command, target.Get<int>()));
            }
            return false;
        }
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

    [Fact]
    public void QueueApply_PreservesConcreteTypeOfAnErasedCommand()
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var observations = Observe<AddAmount>(world);
        var command = new AddAmount(7);
        ICommand erased = command;

        world.Commands.Enqueue(new CommandRequest(target, erased));

        Assert.Empty(observations);
        Assert.Equal(10, target.Get<int>());
        Assert.Equal(1, world.Commands.Apply(world));
        Assert.Equal(17, target.Get<int>());
        Assert.Equal((target, command, 17), Assert.Single(observations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActorPost_NotifiesConcreteListenerAfterApplyingCommand(bool useTypedFactory)
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var observations = Observe<AddAmount>(world);
        var command = new AddAmount(7);
        using var actor = new WorldActor(world);

        if (useTypedFactory) {
            world.Post(CommandRequest.Create(target, command));
        } else {
            actor.Post(new CommandRequest(target, command));
        }
        actor.Complete();
        actor.Run();

        Assert.Null(actor.Failure);
        Assert.Equal(17, target.Get<int>());
        Assert.Equal((target, command, 17), Assert.Single(observations));
    }

    [Fact]
    public void QueueApply_DispatchesTheReplacementCommandType()
    {
        using var world = new World();
        var target = world.Create(HList.From(10));
        var additions = Observe<AddAmount>(world);
        var multiplications = Observe<MultiplyBy>(world);
        var original = new AddAmount(7);
        var replacement = new MultiplyBy(3);
        var request = new CommandRequest(target, original);

        world.Commands.Enqueue(request);
        world.Commands.Enqueue(request with { Command = replacement });

        Assert.Equal(2, world.Commands.Apply(world));
        Assert.Equal(51, target.Get<int>());
        Assert.Equal((target, original, 17), Assert.Single(additions));
        Assert.Equal((target, replacement, 51), Assert.Single(multiplications));
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
        Assert.Equal(new[] {
            (typeof(ReplayCommand), target, 1, 1),
            (typeof(ReplayCommand), target, 2, 3)
        }, observer.Calls.Select(call => (
            call.EventType, call.Target, Assert.IsType<ReplayCommand>(call.Command).Runs, call.Value)));
    }

    [Fact]
    public void TypedRequest_ReplaysMutableStructWithTheSameValueSemanticsAsDirectExecute()
    {
        using var world = new World();
        var direct = world.Create(HList.From(0));
        var queued = world.Create(HList.From(0));
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

        world.Execute<BaseCommand>(first, command);
        world.Execute<DerivedCommand>(first, command);

        var baseRequest = CommandRequest.Create<BaseCommand>(first, command);
        var derivedRequest = CommandRequest.Create<DerivedCommand>(first, command);
        (baseRequest with { Target = second }).Apply(world);
        derivedRequest.Apply(world);

        Assert.Equal(new[] {
            (typeof(BaseCommand), first, 3),
            (typeof(DerivedCommand), first, 6),
            (typeof(BaseCommand), second, 3),
            (typeof(DerivedCommand), first, 9)
        }, observer.Calls.Select(call => (call.EventType, call.Target, call.Value)));
        Assert.All(observer.Calls, call => Assert.Same(command, call.Command));

        var erasedRequest = new CommandRequest(first, command);
        Assert.NotEqual(baseRequest, erasedRequest);
        Assert.Equal(derivedRequest, erasedRequest);
        Assert.Equal(derivedRequest.GetHashCode(), erasedRequest.GetHashCode());
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
