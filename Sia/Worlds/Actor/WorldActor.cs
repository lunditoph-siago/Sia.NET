namespace Sia;

using System.Diagnostics;

public sealed class WorldActor : IDisposable
{
    [ThreadStatic]
    internal static WorldActor? Current;

    private readonly object _gate = new();
    private readonly BlockingQueue<IWorldMessage> _mailbox = new();
    private readonly ManualResetEventSlim _completion = new(false);
    private readonly ManualResetEventSlim _running = new(false);

    private readonly ActorRuntime? _runtime;

    private int _ownerThreadId;
    private int _completePosted;
    private int _completed;
    private int _completionSeen;
    private bool _scheduled;
    private bool _suspended;
    private Exception? _failure;

    public World World { get; }
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;
    public Exception? Failure => _failure;

    internal ActorRuntime? Runtime => _runtime;

    public bool IsOwnerThread
        => ReferenceEquals(Current, this)
        || Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerThreadId);

    public WorldActor(World world)
        : this(world, null) {}

    internal WorldActor(World world, ActorRuntime? runtime)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        _runtime = runtime;
        world.BindActor(this);
    }

    public void Post(IWorldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate) {
            ThrowIfClosed();
            _mailbox.Enqueue(message);
            TryScheduleUnderLock();
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
            TryScheduleUnderLock();
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
        var previous = Current;
        Current = this;

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

                if (message is IAsyncWorldMessage asyncMessage) {
                    asyncMessage.ExecuteAsync(new WorldContext(World, cancellation))
                        .GetAwaiter().GetResult();
                }
                else if (message is IBlockingWorldMessage blockingMessage) {
                    blockingMessage.ExecuteBlocking(
                        new WorldContext(World, cancellation));
                }
                else {
                    message!.Execute(new WorldContext(World, cancellation));
                }
            }
        }
        catch (Exception error) {
            _failure = error;
        }
        finally {
            Volatile.Write(ref _completed, 1);
            _completion.Set();
            Current = previous;
        }
    }

    internal void ProcessBatch()
    {
        var runtime = _runtime!;
        var context = new WorldContext(World);
        var previous = Current;
        Current = this;

        try {
            for (var i = 0; i < runtime.BatchSize; i++) {
                if (!_mailbox.TryDequeue(out var message)) {
                    break;
                }

                if (ReferenceEquals(message, CompleteMessage.Instance)) {
                    Volatile.Write(ref _completionSeen, 1);
                    continue;
                }

                if (message is IAsyncWorldMessage asyncMessage) {
                    SuspendForAsync();
                    try {
                        var task = asyncMessage.ExecuteAsync(context);
                        runtime.ContinueAsync(this, task);
                    }
                    catch (Exception error) {
                        ResumeFromSuspension(error);
                    }
                    return;
                }

                if (message is IBlockingWorldMessage blockingMessage) {
                    SuspendForAsync();
                    try {
                        runtime.ExecuteBlocking(this, blockingMessage, context);
                    }
                    catch (Exception error) {
                        ResumeFromSuspension(error);
                    }
                    return;
                }

                try {
                    message!.Execute(context);
                }
                catch (Exception error) {
                    Fail(error);
                    return;
                }
            }

            CompleteBatch();
        }
        finally {
            Current = previous;
        }
    }

    private void SuspendForAsync()
    {
        lock (_gate) {
            _suspended = true;
            _scheduled = false;
        }
    }

    internal void ResumeFromSuspension(Exception? error = null)
    {
        lock (_gate) {
            _suspended = false;

            if (error != null) {
                _failure = error;
                CompleteUnderLock();
                return;
            }

            if (Volatile.Read(ref _completionSeen) != 0
                && !_mailbox.TryPeek(out _)) {
                CompleteUnderLock();
                return;
            }

            if (_mailbox.TryPeek(out _)) {
                ScheduleUnderLock();
            }
            else {
                _scheduled = false;
            }
        }
    }

    private void CompleteBatch()
    {
        lock (_gate) {
            if (_suspended) {
                return;
            }

            if (Volatile.Read(ref _completionSeen) != 0
                && !_mailbox.TryPeek(out _)) {
                CompleteUnderLock();
                return;
            }

            if (_mailbox.TryPeek(out _)) {
                ScheduleUnderLock();
            }
            else {
                _scheduled = false;
            }
        }
    }

    private void Fail(Exception error)
    {
        lock (_gate) {
            _failure = error;
            _suspended = false;
            _scheduled = false;
            CompleteUnderLock();
        }
    }

    private void TryScheduleUnderLock()
    {
        if (_runtime != null
            && !_suspended
            && !_scheduled
            && Volatile.Read(ref _completed) == 0) {
            ScheduleUnderLock();
        }
    }

    private void ScheduleUnderLock()
    {
        _scheduled = true;
        _runtime!.Schedule(this);
    }

    private void CompleteUnderLock()
    {
        if (Volatile.Read(ref _completed) != 0) {
            return;
        }
        _completed = 1;
        _completion.Set();
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _completed) != 0
            || (Volatile.Read(ref _completePosted) != 0 && !IsOwnerThread)) {
            throw new ObjectDisposedException(nameof(WorldActor));
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
