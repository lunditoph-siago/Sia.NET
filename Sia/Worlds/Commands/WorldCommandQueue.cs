using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Sia;

public readonly record struct CommandRequest
{
    public Entity Target { get; init; }

    public required ICommand Command {
        get;
        init {
            field = value;
            Executor = Execute<ICommand>;
        }
    }

    public Action<World, Entity, ICommand> Executor { get; init; }

    [SetsRequiredMembers]
    public CommandRequest(Entity Target, ICommand Command)
        => this = Create<ICommand>(Target, Command);

    public static CommandRequest Create<TCommand>(Entity target, in TCommand command)
        where TCommand : ICommand
        => new() {
            Target = target,
            Command = command,
            Executor = Execute<TCommand>
        };

    public static void Execute<TCommand>(World world, Entity target, ICommand command)
        where TCommand : ICommand
        => world.Execute<TCommand>(target, (TCommand)command);
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
        if (_requests.IsEmpty) return 0;

        world.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(world.IsDisposed, world);

        var count = 0;
        while (_requests.TryDequeue(out var request)) {
            request.Executor(world, request.Target, request.Command);
            count++;
        }
        return count;
    }
}
