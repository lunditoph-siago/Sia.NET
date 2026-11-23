using System.Collections.Concurrent;

namespace Sia;

public readonly record struct CommandRequest
{
    private readonly ICommand _command;
    private readonly Action<World, Entity, ICommand> _execute;

    public Entity Target { get; init; }

    /// <summary>Replacing the command selects <see cref="ICommand"/> for event delivery.</summary>
    public ICommand Command
    {
        get => _command;
        init {
            ArgumentNullException.ThrowIfNull(value);
            _command = value;
            _execute = Execute<ICommand>;
        }
    }

    /// <summary>Creates a request that publishes <see cref="ICommand"/> when applied.</summary>
    public CommandRequest(Entity Target, ICommand Command)
        : this(Target, Command, Execute<ICommand>) {}

    private CommandRequest(Entity target, ICommand command, Action<World, Entity, ICommand> execute)
    {
        ArgumentNullException.ThrowIfNull(command);
        Target = target;
        _command = command;
        _execute = execute;
    }

    /// <summary>Creates a request that dispatches the declared <typeparamref name="TCommand"/> when applied.</summary>
    /// <remarks>An argument declared as <see cref="ICommand"/> selects <see cref="ICommand"/> for event delivery.</remarks>
    public static CommandRequest Create<TCommand>(Entity target, in TCommand command)
        where TCommand : ICommand
        => new(target, command, Execute<TCommand>);

    private static void Execute<TCommand>(World world, Entity target, ICommand command)
        where TCommand : ICommand
        => world.Execute<TCommand>(target, (TCommand)command);

    public void Apply(World world) => _execute(world, Target, Command);

    public void Deconstruct(out Entity Target, out ICommand Command)
        => (Target, Command) = (this.Target, this.Command);
}

public interface IWorldCommandQueue
{
    int Count { get; }

    void Enqueue(in CommandRequest request);
    int Apply(World world);
}

public sealed class WorldCommandQueue : IWorldCommandQueue
{
    private readonly ConcurrentQueue<CommandRequest> _requests = new();

    public int Count => _requests.Count;

    public void Enqueue(in CommandRequest request)
        => _requests.Enqueue(request);

    public int Apply(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_requests.IsEmpty) return 0;

        world.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(world.IsDisposed, world);

        var count = 0;
        while (_requests.TryDequeue(out var request)) {
            request.Apply(world);
            count++;
        }
        return count;
    }
}
