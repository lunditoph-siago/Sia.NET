using System.Runtime.ExceptionServices;

namespace Sia;

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
    private int _completionRequested;
    private int _completed;
    private int _completionMessageRead;
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
        : this(world, null) { }

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
        if (ReferenceEquals(message, CompleteMessage.Instance)) {
            Complete();
            return;
        }

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

    public void PostTickSchedule(Scheduler scheduler, ScheduleLabel? label = null)
        => Post(new SchedulerTickMessage(scheduler, label));

    public void Complete()
    {
        lock (_gate) {
            if (Volatile.Read(ref _completionRequested) != 0
                || Volatile.Read(ref _completed) != 0) {
                return;
            }
            _completionRequested = 1;
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
        var previousActor = Current;
        Current = this;

        var completionMessageRead = false;
        Exception? failure = null;

        try {
            while (true) {
                if (!_mailbox.TryDequeue(out var message)) {
                    if (completionMessageRead) {
                        break;
                    }
                    if (!_mailbox.Dequeue(out message)) {
                        continue;
                    }
                }

                if (ReferenceEquals(message, CompleteMessage.Instance)) {
                    completionMessageRead = true;
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
            failure = error;
        }
        finally {
            Current = previousActor;
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
        var previousActor = Current;
        Current = this;
        if (!_running.IsSet) {
            _running.Set();
        }

        try {
            for (var i = 0; i < runtime.BatchSize; i++) {
                if (!_mailbox.TryDequeue(out var message)) {
                    break;
                }

                if (ReferenceEquals(message, CompleteMessage.Instance)) {
                    Volatile.Write(ref _completionMessageRead, 1);
                    continue;
                }

                if (message is IAsyncWorldMessage asyncMessage) {
                    SuspendMailbox();
                    try {
                        var task = asyncMessage.ExecuteAsync(context);
                        runtime.ResumeAfterAsync(this, task);
                    }
                    catch (Exception error) {
                        ResumeFromSuspension(error);
                    }
                    return;
                }

                if (message is IBlockingWorldMessage blockingMessage) {
                    SuspendMailbox();
                    try {
                        runtime.ScheduleBlocking(this, blockingMessage, context);
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

            FinishBatch();
        }
        finally {
            Current = previousActor;
        }
    }

    private void SuspendMailbox()
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
                FailUnderLock(error);
                return;
            }

            ContinueOrCompleteUnderLock();
        }
    }

    private void FinishBatch()
    {
        lock (_gate) {
            if (_suspended) {
                return;
            }

            ContinueOrCompleteUnderLock();
        }
    }

    private void ContinueOrCompleteUnderLock()
    {
        if (Volatile.Read(ref _completionMessageRead) != 0
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

    private void Fail(Exception error)
    {
        lock (_gate) {
            FailUnderLock(error);
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
                case SchedulerTickMessage schedulerTick:
                    schedulerTick.Reject(error);
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
        _runtime?.OnActorCompleted(this);
        _completion.Set();
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _completed) != 0
            || (Volatile.Read(ref _completionRequested) != 0 && !IsOwnerThread)) {
            throw new ObjectDisposedException(nameof(WorldActor));
        }
    }

    public bool WaitUntilRunning(TimeSpan timeout)
        => _running.WaitHandle.WaitOne(ToWaitMilliseconds(timeout));

    public bool WaitForCompletion(TimeSpan timeout)
        => _completion.WaitHandle.WaitOne(ToWaitMilliseconds(timeout));

    private static int ToWaitMilliseconds(TimeSpan timeout)
        => timeout.TotalMilliseconds >= int.MaxValue
            ? Timeout.Infinite
            : (int)timeout.TotalMilliseconds;

    public void ThrowIfFailed()
    {
        if (Failure is { } failure) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public void Dispose()
    {
        Complete();
        GC.SuppressFinalize(this);
    }

    public void CompleteAndDispose(TimeSpan timeout)
    {
        if (!IsCompleted
            && (IsOwnerThread || (_runtime is not null && Current?.Runtime == _runtime))) {
            throw new InvalidOperationException(
                "A runtime worker cannot wait for an actor on the same runtime.");
        }

        Complete();
        if (!WaitForCompletion(timeout)) {
            throw new TimeoutException(
                $"WorldActor did not drain within {timeout}.");
        }
        Outcome<Exception>.Success
            .Attempt(ThrowIfFailed)
            .Attempt(World.Dispose)
            .ThrowIfFailed();
    }
}
