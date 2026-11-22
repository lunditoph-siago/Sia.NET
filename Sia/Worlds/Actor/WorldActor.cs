namespace Sia;

public sealed class WorldActor : IDisposable
{
    [ThreadStatic]
    private static WorldActor? s_current;

    [ThreadStatic]
    internal static WorldActor? BlockingActor;

    [ThreadStatic]
    private static Stack<(WorldActor Actor, bool HoldsWorker)>? s_enclosingActors;

    private sealed class AsyncExecution(
        WorldActor actor, AsyncExecution? previous,
        (WorldActor Actor, bool HoldsWorker)[]? enclosingActors)
    {
        public WorldActor Actor { get; } = actor;
        public AsyncExecution? Previous { get; } = previous;
        public (WorldActor Actor, bool HoldsWorker)[]? EnclosingActors { get; } = enclosingActors;
        public Task? Completion;
    }

    private static readonly AsyncLocal<AsyncExecution?> s_asyncExecution = new();

    internal static Task StartWorker(Action action, TaskCreationOptions options)
    {
        var previous = s_asyncExecution.Value;
        s_asyncExecution.Value = null;
        try {
            return Task.Factory.StartNew(action, CancellationToken.None, options, TaskScheduler.Default);
        }
        finally {
            s_asyncExecution.Value = previous;
        }
    }

    internal static bool BlocksShutdownWait(
        ActorRuntime? runtime, WorldActor? actor, out bool holdsRuntimeWorker)
    {
        var workerInUse = false;
        bool Matches(WorldActor? candidate, bool holdsWorker = false)
        {
            if (candidate is null) return false;
            if (ReferenceEquals(candidate, actor)) return true;
            if (runtime is null || !ReferenceEquals(candidate._runtime, runtime)) return false;
            workerInUse |= holdsWorker;
            return actor is null;
        }

        holdsRuntimeWorker = false;
        if (Matches(s_current, holdsWorker: true) || Matches(BlockingActor)) {
            return true;
        }
        if (s_enclosingActors is { } enclosingActors) {
            foreach (var (enclosing, holdsWorker) in enclosingActors) {
                if (Matches(enclosing, holdsWorker)) return true;
            }
        }
        for (var execution = s_asyncExecution.Value; execution is not null; execution = execution.Previous) {
            if (Volatile.Read(ref execution.Completion)?.IsCompleted == true) continue;
            if (Matches(execution.Actor)) return true;
            if (execution.EnclosingActors is { } actors) {
                foreach (var (enclosing, holdsWorker) in actors) {
                    if (Matches(enclosing, holdsWorker)) return true;
                }
            }
        }
        holdsRuntimeWorker = workerInUse;
        return false;
    }

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
    private Task? _pendingAsync;
#if !BROWSER
    private Action? _resumeAsync;
#endif
    private Exception? _failure;

    public World World { get; }
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;
    public Exception? Failure => Volatile.Read(ref _failure);

    public bool IsOwnerThread
        => ReferenceEquals(s_current, this)
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
        var previous = s_current;
        var enclosing = previous ?? BlockingActor;
        if (enclosing is not null) {
            (s_enclosingActors ??= new()).Push((enclosing, previous?._runtime is not null));
        }
        s_current = this;

        var context = new WorldContext(World, cancellation);
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
                    var task = ExecuteAsync(asyncMessage, context);
                    try {
                        task.GetAwaiter().GetResult();
                    }
                    catch (Exception error) {
                        failure = task.Exception is { InnerExceptions.Count: > 1 } errors ? errors : error;
                        break;
                    }
                }
                else if (message is IBlockingWorldMessage blockingMessage) {
                    blockingMessage.ExecuteBlocking(context);
                }
                else {
                    message!.Execute(context);
                }
            }
        }
        catch (Exception error) {
            failure = error;
        }
        finally {
            s_current = previous;
            if (enclosing is not null) s_enclosingActors!.Pop();
            lock (_gate) {
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
        var previous = s_current;
        s_current = this;
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
                        var task = _pendingAsync = ExecuteAsync(asyncMessage, context);
                        if (task.IsCompleted) {
                            ResumeAsync();
                        }
                        else {
#if BROWSER
                            _ = task.ContinueWith(static (_, state) => ((WorldActor)state!).ResumeAsync(),
                                this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
#else
                            task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(_resumeAsync ??= ResumeAsync);
#endif
                        }
                    }
                    catch (Exception error) {
                        _pendingAsync = null;
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
            s_current = previous;
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
        var enclosingActors = s_enclosingActors is { Count: > 0 } actors ? actors.ToArray() : null;
        var execution = new AsyncExecution(this, previous, enclosingActors);
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

    private void ResumeAsync()
    {
        var task = _pendingAsync!;
        _pendingAsync = null;
        var error = task.Exception;
        ResumeFromSuspension(task.IsCanceled
            ? new TaskCanceledException(task)
            : error is { InnerExceptions.Count: 1 } ? error.InnerException : error);
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
        => WaitForCompletion(ToTimeout(timeout), complete: false);

    private bool WaitForCompletion(int timeout, bool complete)
    {
        var canBlock = timeout > 0 || timeout == Timeout.Infinite;
        var holdsRuntimeWorker = false;
        if ((complete || canBlock) && !IsCompleted
            && (IsOwnerThread || BlocksShutdownWait(_runtime, this, out holdsRuntimeWorker))) {
            throw new InvalidOperationException("An actor cannot wait for its own completion.");
        }

#if !BROWSER
        var runtime = holdsRuntimeWorker && canBlock && !IsCompleted ? _runtime : null;
        if (runtime is not null && !runtime.TryEnterWorkerWait()) {
            if (!IsCompleted) {
                throw new InvalidOperationException("Waiting would leave no runtime worker to complete the actor.");
            }
            runtime = null;
        }

        try {
#endif
            if (complete) Complete();
            return _completion.WaitHandle.WaitOne(timeout);
#if !BROWSER
        }
        finally {
            runtime?.ExitWorkerWait();
        }
#endif
    }

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
        if (!WaitForCompletion(ToTimeout(timeout), complete: true)) {
            throw new TimeoutException(
                $"WorldActor did not drain within {timeout}.");
        }
        Outcome<Exception>.Success.Attempt(ThrowIfFailed).Attempt(World.Dispose).ThrowIfFailed();
    }
}
