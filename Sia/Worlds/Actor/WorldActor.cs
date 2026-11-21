namespace Sia;

public sealed class WorldActor : IDisposable
{
    [ThreadStatic]
    internal static WorldActor? Current;

    private sealed class AsyncExecution(WorldActor actor)
    {
        public WorldActor Actor { get; } = actor;
        public Task? Completion;
    }

    private static readonly AsyncLocal<AsyncExecution?> s_asyncExecution = new();

    internal static WorldActor? ExecutingActor
        => Current ?? (s_asyncExecution.Value is { } execution
            && Volatile.Read(ref execution.Completion)?.IsCompleted != true ? execution.Actor : null);

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
    public Exception? Failure => Volatile.Read(ref _failure);

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
            if (ReferenceEquals(message, CompleteMessage.Instance)) {
                _completePosted = 1;
            }
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

    public void PostTickSchedule(Scheduler scheduler, ScheduleLabel? label = null)
        => Post(new SchedulerTickMessage(scheduler, label));

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
        if (_runtime is not null) {
            throw new InvalidOperationException("A pooled world actor runs on its runtime workers.");
        }
        lock (_gate) {
            ObjectDisposedException.ThrowIf(IsCompleted, this);
            if (_ownerThreadId != 0) {
                throw new InvalidOperationException("The world actor is already running.");
            }
            Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        }

        _running.Set();
        var previous = Current;
        Current = this;

        var completionSeen = false;
        Exception? failure = null;

        try {
            while (true) {
                if (!_mailbox.TryDequeue(out var message)) {
                    if (completionSeen) {
                        break;
                    }
                    if (!_mailbox.Dequeue(out message)) continue;
                }

                if (ReferenceEquals(message, CompleteMessage.Instance)) {
                    completionSeen = true;
                    continue;
                }

                if (message is IAsyncWorldMessage asyncMessage) {
                    var task = ExecuteAsync(asyncMessage, new WorldContext(World, cancellation));
                    try {
                        task.GetAwaiter().GetResult();
                    }
                    catch (Exception error) {
                        failure = task.Exception is { InnerExceptions.Count: > 1 } errors ? errors : error;
                        break;
                    }
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
            failure = error;
        }
        finally {
            Current = previous;
            lock (_gate) {
                Volatile.Write(ref _ownerThreadId, 0);
                if (failure is null) {
                    CompleteUnderLock();
                }
                else {
                    FailUnderLock(failure);
                }
            }
        }
    }

    internal void ProcessBatch()
    {
        var runtime = _runtime!;
        var context = new WorldContext(World);
        var previous = Current;
        Current = this;
        if (!_running.IsSet) _running.Set();

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
                        var task = ExecuteAsync(asyncMessage, context);
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
                    lock (_gate) {
                        FailUnderLock(error);
                    }
                    return;
                }
            }

            lock (_gate) {
                ContinueOrCompleteUnderLock();
            }
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

    private Task ExecuteAsync(IAsyncWorldMessage message, in WorldContext context)
    {
        var previous = s_asyncExecution.Value;
        var execution = new AsyncExecution(this);
        s_asyncExecution.Value = execution;
        Task? completion = null;
        try {
            return completion = message.ExecuteAsync(context);
        }
        finally {
            Volatile.Write(ref execution.Completion, completion ?? Task.CompletedTask);
            s_asyncExecution.Value = previous;
        }
    }

    internal void ResumeFromSuspension(Exception? error = null)
    {
        lock (_gate) {
            _suspended = false;

            if (error != null) {
                FailUnderLock(error);
                return;
            }

            ContinueOrCompleteUnderLock();
        }
    }

    private void ContinueOrCompleteUnderLock()
    {
        if (_mailbox.TryPeek(out _)) {
            ScheduleUnderLock();
        }
        else {
            _scheduled = false;
            if (Volatile.Read(ref _completionSeen) != 0) {
                CompleteUnderLock();
            }
        }
    }

    private void FailUnderLock(Exception error)
    {
        Volatile.Write(ref _failure, error);
        _suspended = false;
        _scheduled = false;
        while (_mailbox.TryDequeue(out var message)) {
            switch (message) {
                case TickMessage tick:
                    tick.Reject(error);
                    break;
                case SchedulerTickMessage schedule:
                    schedule.Reject(error);
                    break;
            }
        }
        CompleteUnderLock();
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
        Volatile.Write(ref _completed, 1);
        _completion.Set();
        _runtime?.OnActorCompleted(this);
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
        if (Failure is { } failure) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure).Throw();
        }
    }

    public void Dispose()
    {
        Complete();
        GC.SuppressFinalize(this);
    }

    public void CompleteAndDispose(TimeSpan timeout)
    {
        if (!IsCompleted) {
            var executing = ExecutingActor;
            if (IsOwnerThread || ReferenceEquals(executing, this)
                || (_runtime is not null && executing?.Runtime == _runtime)) {
                throw new InvalidOperationException("Cannot wait for actor completion from the same actor or runtime.");
            }
        }
        Complete();
        if (!WaitForCompletion(timeout)) {
            throw new TimeoutException(
                $"WorldActor did not drain within {timeout}.");
        }
        Outcome<Exception>.Success.Attempt(ThrowIfFailed).Attempt(World.Dispose).ThrowIfFailed();
    }
}
