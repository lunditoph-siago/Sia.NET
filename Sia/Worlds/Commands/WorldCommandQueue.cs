using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sia;

public readonly record struct CommandRequest
{
    private delegate void Executor(World world, Entity target, ICommand command);
    private const string ErasedCommandMessage =
        "Resolving an erased command requires a runtime generic instantiation. Use CommandRequest.Create with the concrete command type.";
    private static readonly ConditionalWeakTable<Type, Executor> s_executors = new();

    private readonly ICommand _command;
    private readonly Executor _execute;

    public Entity Target { get; init; }

    /// <summary>Replacing the command selects its runtime type for event delivery.</summary>
    public ICommand Command
    {
        get => _command;
        [RequiresDynamicCode(ErasedCommandMessage)]
        init => (_command, _execute) = (value, GetBoxedExecutor(value));
    }

    /// <summary>Creates a request that executes the supplied instance and publishes its runtime type when applied.</summary>
    [RequiresDynamicCode(ErasedCommandMessage)]
    public CommandRequest(Entity Target, ICommand Command)
        : this(Target, Command, GetBoxedExecutor(Command)) {}

    private CommandRequest(Entity target, ICommand command, Executor execute)
        => (Target, _command, _execute) = (target, command, execute);

    /// <summary>Creates a request that uses <typeparamref name="TCommand"/> for execution and event delivery when applied.</summary>
    public static CommandRequest Create<TCommand>(Entity target, in TCommand command)
        where TCommand : ICommand
        => new(target, command, typeof(TCommand).IsValueType
            ? ExecuteValue<TCommand>
            : ExecuteBoxed<TCommand>);

    private static void ExecuteValue<TCommand>(World world, Entity target, ICommand command)
        where TCommand : ICommand
        => world.Execute(target, (TCommand)command);

    [RequiresDynamicCode(ErasedCommandMessage)]
    private static Executor GetBoxedExecutor(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return s_executors.GetValue(command.GetType(), static type =>
            typeof(CommandRequest).GetMethod(nameof(ExecuteBoxed), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(type).CreateDelegate<Executor>());
    }

    private static void ExecuteBoxed<TCommand>(World world, Entity target, ICommand command)
        where TCommand : ICommand
    {
        // A mutable struct in an existing ICommand box must update that same box on every replay.
        world.ApplyCommand(target, command);
        world.Dispatcher.Send(target, (TCommand)command);
    }

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
