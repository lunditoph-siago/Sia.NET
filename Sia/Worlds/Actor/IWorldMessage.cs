namespace Sia;

public interface IWorldMessage
{
    void Execute(in WorldContext context);
}

public interface IAsyncWorldMessage : IWorldMessage
{
    Task ExecuteAsync(in WorldContext context);
}

public interface IBlockingWorldMessage : IWorldMessage
{
    void ExecuteBlocking(in WorldContext context);
}

public readonly record struct CommandMessage(CommandRequest Request) : IWorldMessage
{
    public readonly void Execute(in WorldContext context)
        => Request.Apply(context.World);
}

public readonly record struct EventMessage<TEvent>(Entity Target, TEvent Event)
    : IWorldMessage
    where TEvent : IEvent
{
    public readonly void Execute(in WorldContext context)
        => context.World.Send(Target, Event);
}

public sealed class TickMessage(SystemStage stage) : IWorldMessage
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SystemStage Stage { get; } = stage;
    public Task Completion => _completion.Task;

    public void Execute(in WorldContext context)
    {
        try {
            Stage.Tick(context.Cancellation);
            _completion.TrySetResult();
        }
        catch (Exception error) {
            _completion.TrySetException(error);
            throw;
        }
    }
}

public sealed class CompleteMessage : IWorldMessage
{
    public static CompleteMessage Instance { get; } = new();

    private CompleteMessage() {}

    public void Execute(in WorldContext context) {}
}
