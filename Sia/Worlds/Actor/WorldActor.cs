namespace Sia;

using System.Diagnostics;

public sealed class WorldActor : IDisposable
{
    private readonly object _gate = new();
    private readonly BlockingQueue<IWorldMessage> _mailbox = new();
    private readonly ManualResetEventSlim _completion = new(false);
    private readonly ManualResetEventSlim _running = new(false);

    private int _ownerThreadId;
    private int _completePosted;
    private int _completed;
    private Exception? _failure;

    public World World { get; }
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;
    public Exception? Failure => _failure;

    public bool IsOwnerThread
        => Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerThreadId);

    public WorldActor(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        world.BindActor(this);
    }

    public void Post(IWorldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate) {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _completePosted) != 0
                || Volatile.Read(ref _completed) != 0,
                this);
            _mailbox.Enqueue(message);
        }
    }

    public void Post(in CommandRequest request)
        => Post(new CommandMessage(request));

    public void Post<TEvent>(Entity target, in TEvent e)
        where TEvent : IEvent
        => Post(new EventMessage<TEvent>(target, e));

    public void PostTick(SystemStage stage)
        => Post(new TickMessage(stage));

    public void Complete()
    {
        lock (_gate) {
            if (Volatile.Read(ref _completePosted) != 0
                || Volatile.Read(ref _completed) != 0) {
                return;
            }
            _completePosted = 1;
            _mailbox.Enqueue(CompleteMessage.Instance);
        }
    }

    public void Run(CancellationToken cancellation = default)
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        var ownerThreadId = Interlocked.CompareExchange(
            ref _ownerThreadId, currentThreadId, 0);

        if (ownerThreadId != 0 && ownerThreadId != currentThreadId) {
            throw new InvalidOperationException(
                "The world actor is already running on another thread.");
        }

        _running.Set();
        var completionSeen = false;

        try {
            while (true) {
                if (!_mailbox.TryDequeue(out var message)) {
                    if (completionSeen) {
                        break;
                    }
                    _mailbox.Dequeue(out message);
                }

                if (ReferenceEquals(message, CompleteMessage.Instance)) {
                    completionSeen = true;
                    continue;
                }

                try {
                    message!.Execute(new WorldContext(World, cancellation));
                }
                catch (Exception error) {
                    _failure = error;
                    break;
                }
            }
        }
        finally {
            Volatile.Write(ref _completed, 1);
            _completion.Set();
        }
    }

    public bool WaitUntilRunning(TimeSpan timeout)
        => _running.WaitHandle.WaitOne(ToTimeout(timeout));

    public bool WaitForCompletion(TimeSpan timeout)
        => _completion.WaitHandle.WaitOne(ToTimeout(timeout));

    private static int ToTimeout(TimeSpan timeout)
        => timeout.TotalMilliseconds >= int.MaxValue
            ? Timeout.Infinite
            : (int)timeout.TotalMilliseconds;

    public void ThrowIfFailed()
    {
        if (_failure is { } failure) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure).Throw();
        }
    }

    public void Dispose()
    {
        Complete();
        GC.SuppressFinalize(this);
    }
}
