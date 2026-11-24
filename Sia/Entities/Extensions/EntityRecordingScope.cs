#pragma warning disable CS8500 // Recording borrows a pinned Span<TResult>, including managed result types.

namespace Sia;

using System.Runtime.CompilerServices;

internal readonly ref struct RecordingScope<TResult, TRunner>
    where TRunner : IRunner
{
    private readonly Span<TResult> _destination;
    private readonly RecordingBuffer<TResult>? _buffer;
    private readonly RunnerBarrier? _callerBarrier;

    public RecordingScope(Span<TResult> destination, int count, TRunner runner, RunnerBarrier? barrier)
    {
        EntityExtensionsCommon.GuardSpanLength(destination, count);
        _destination = destination[..count];
        var parallel = runner is ParallelRunner candidate ? candidate : null;
        var inline = runner is CurrentThreadRunner || parallel?.IsCurrentWorker == true;
        if (count != 0 && !inline && parallel?.GetType() != typeof(ParallelRunner)) {
            _buffer = new(_destination.ToArray());
        }
        Runner = new(runner, inline, _buffer);
        if (count != 0 && !inline && barrier != null) {
            barrier.AddParticipants(1);
            _callerBarrier = barrier;
        }
    }

    public bool IsEmpty => _destination.IsEmpty;
    public ScopedRecordingRunner<TRunner> Runner { get; }
    public Span<TResult> DirectDestination => _buffer == null ? _destination : default;

    public unsafe RecordingOutput<TResult> GetOutput(TResult* pointer, int* index)
        => _buffer == null
            ? new(pointer, index, _destination.Length, Runner.IsInline)
            : new(_buffer);

    public void Dispose()
    {
        try {
            if (_buffer is { } buffer) {
                buffer.Results.AsSpan().CopyTo(_destination);
            }
        }
        finally {
            _callerBarrier?.Signal();
        }
    }
}

internal readonly unsafe struct RecordingOutput<TResult>
{
    private readonly TResult* _pointer;
    private readonly int* _cursor;
    private readonly int _length;
    private readonly bool _inline;
    private readonly RecordingBuffer<TResult>? _buffer;

    public RecordingOutput(TResult* pointer, int* cursor, int length, bool inline)
    {
        _pointer = pointer;
        _cursor = cursor;
        _length = length;
        _inline = inline;
    }

    public RecordingOutput(RecordingBuffer<TResult> buffer) => _buffer = buffer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TResult Next()
    {
        if (_buffer is { } buffer) {
            return ref buffer.Next();
        }
        var index = _inline ? ++*_cursor : Interlocked.Increment(ref *_cursor);
        if ((uint)index >= (uint)_length) {
            throw new InvalidOperationException("Recording exceeded the destination length");
        }
        return ref _pointer[index];
    }
}

internal class RecordingLease
{
    private const int Closed = int.MinValue;
    private int _state;
    private int _completed;

    public bool TryEnter()
    {
        while (true) {
            var state = Volatile.Read(ref _state);
            if (state < 0) {
                return false;
            }
            if (Interlocked.CompareExchange(ref _state, state + 1, state) == state) {
                return true;
            }
        }
    }

    public void Exit(int completed)
    {
        Interlocked.Add(ref _completed, completed);
        Interlocked.Decrement(ref _state);
    }

    public int Close()
    {
        // The sign bit closes admission. The other bits count callbacks still
        // allowed to borrow host storage. Close admission before any wait.
        Interlocked.Or(ref _state, Closed);
        ThreadInterruptedException? interruption = null;
        var spinner = new SpinWait();
        while (Volatile.Read(ref _state) != Closed) {
            try {
                spinner.SpinOnce();
            }
            catch (ThreadInterruptedException exception) {
                interruption ??= exception;
            }
        }
        if (interruption != null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(interruption);
        }
        return Volatile.Read(ref _completed);
    }
}

internal sealed class RecordingBuffer<TResult>(TResult[] results) : RecordingLease
{
    private int _index = -1;

    public TResult[] Results { get; } = results;

    public ref TResult Next() => ref Results[Interlocked.Increment(ref _index)];
}

internal readonly struct ScopedRecordingRunner<TRunner>(TRunner runner, bool inline, RecordingLease? lease)
    : IRunner where TRunner : IRunner
{
    private readonly record struct ScopedJob<TData>(
        int Count, TData Data, GroupAction<TData> Action, RecordingLease Lease);

    public readonly bool IsInline = inline;

    public void Run<TData>(int taskCount, in TData data, GroupAction<TData> action, RunnerBarrier? barrier)
    {
        if (IsInline) {
            CurrentThreadRunner.Instance.Run(taskCount, data, action, barrier);
        }
        else if (lease is { } gate) {
            int completed;
            // Foreign runners may retain a barrier even after a failed submission.
            // Keep their barrier unpooled and stop late jobs before they borrow host storage.
            var completion = new RunnerBarrier();
            try {
                runner.Run(taskCount, new ScopedJob<TData>(taskCount, data, action, gate), Invoke, completion);
                completion.Wait();
            }
            finally {
                completed = gate.Close();
            }
            if (completed != taskCount) {
                throw new InvalidOperationException("The runner did not complete the recording work");
            }
        }
        else {
            var completion = RunnerBarrier.GetForRecording();
            try {
                runner.Run(taskCount, data, action, completion);
            }
            catch {
                try {
                    completion.WaitAndReturnRecording();
                }
                catch {
                    // Preserve the submission failure after draining accepted work.
                }
                throw;
            }
            completion.WaitAndReturnRecording();
        }
    }

    private static void Invoke<TData>(in ScopedJob<TData> job, (int, int) range)
    {
        if (!job.Lease.TryEnter()) {
            return;
        }
        var completed = 0;
        try {
            var (from, to) = range;
            if ((uint)from > (uint)to || (uint)to > (uint)job.Count) {
                throw new ArgumentOutOfRangeException(nameof(range));
            }
            job.Action(job.Data, range);
            completed = to - from;
        }
        finally {
            job.Lease.Exit(completed);
        }
    }

    public void Run(Action action, RunnerBarrier? barrier)
        => Run(1, action, static (in Action callback, (int, int) _) => callback(), barrier);

    public void Run<TData>(in TData data, InAction<TData> action, RunnerBarrier? barrier)
        => Run(1, (data, action),
            static (in (TData Data, InAction<TData> Action) entry, (int, int) _) => entry.Action(entry.Data), barrier);

    public void Run(int taskCount, GroupAction action, RunnerBarrier? barrier)
        => Run(taskCount, action, static (in GroupAction callback, (int, int) range) => callback(range), barrier);

    public void Dispose() { }
}

#pragma warning restore CS8500
