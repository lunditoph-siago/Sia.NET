#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

using static Sia.EntityExtensionsCommon;

namespace Sia;

public static partial class EntityHostExtensions
{
    #region Record

    public static unsafe void Record<TRunner, TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new EntityRecordData<TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.Handle(data,
                static (IEntityHost host, in EntityRecordData<TResult> data, int from, int to) => {
                    var recorder = data.Recorder;

                    for (var i = from; i != to; ++i) {
                        recorder(host.GetEntity(i),
                            out data.Output.Next());
                    }
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void Record<TRunner, TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new EntityRecordData<TData, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.Handle(data,
                static (IEntityHost host, in EntityRecordData<TData, TResult> data, int from, int to) => {
                    var recorder = data.Recorder;
                    ref readonly var userData = ref data.UserData;

                    for (var i = from; i != to; ++i) {
                        recorder(userData, host.GetEntity(i),
                            out data.Output.Next());
                    }
                }, recording.Runner, barrier: null);
        }
    }

    private static void IdEntityRecorder(Entity entity, out Entity result)
        => result = entity;

    public static void Record<TRunner>(
        this IEntityHost host, Span<Entity> span, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        if (runner is CurrentThreadRunner) {
            host.Record(span);
            return;
        }
        host.Record(span, IdEntityRecorder, runner, barrier);
    }

    #region CurrentThreadRunner

    public static void Record<TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder)
        => host.Record(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void Record<TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void Record(
        this IEntityHost host, Span<Entity> span)
    {
        var count = host.Count;
        GuardSpanLength(span, count);
        if (count == 0) {
            return;
        }
        var destination = span[..count];
        var version = host.Version;
        for (var index = 0; index < destination.Length; index++) {
            destination[index] = host.GetEntity(index);
        }
        GuardVersion(version, host.Version);
    }

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public static void RecordOnParallel<TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder)
        => host.Record(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordOnParallel<TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordOnParallel(
        this IEntityHost host, Span<Entity> span)
        => host.Record(span, IdEntityRecorder, ParallelRunner.Default, barrier: null);

    #endregion // ParallelRunner

    #endregion // Record

    #region RecordSlices

    public static unsafe void RecordSlices<TRunner, C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, TResult> data, ref C1 c1) => {
                    data.Recorder(ref c1,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, C2, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(ref c1, ref c2,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, C2, C3, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(ref c1, ref c2, ref c3,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, C2, C3, C4, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, C6, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, TResult> data, ref C1 c1) => {
                    data.Recorder(data.UserData,
                        ref c1,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, C2, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, C2, C3, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    #region CurrentThreadRunner

    public static void RecordSlices<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public static void RecordSlicesOnParallel<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    #endregion // ParallelRunner

    #endregion // RecordSlices

    #region RecordSlices_WithEntity

    public static unsafe void RecordSlices<TRunner, C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, TResult> data, ref C1 c1) => {
                    data.Recorder(entity,
                        ref c1,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, C2, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(entity,
                        ref c1, ref c2,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, C2, C3, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(entity,
                        ref c1, ref c2, ref c3,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, C2, C3, C4, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(entity,
                        ref c1, ref c2, ref c3, ref c4,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, C2, C3, C4, C5, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(entity,
                        ref c1, ref c2, ref c3, ref c4, ref c5,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new CompRecordDataWithEntity<C1, C2, C3, C4, C5, C6, TResult> {
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in CompRecordDataWithEntity<C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(entity,
                        ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, TResult> data, ref C1 c1) => {
                    data.Recorder(entity, data.UserData,
                        ref c1,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, C2, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(entity, data.UserData,
                        ref c1, ref c2,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, C2, C3, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(entity, data.UserData,
                        ref c1, ref c2, ref c3,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(entity, data.UserData,
                        ref c1, ref c2, ref c3, ref c4,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(entity, data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        using var recording = new RecordingScope<TResult, TRunner>(span, host.Count, runner, barrier);
        if (recording.IsEmpty) {
            return;
        }

        var index = -1;
        fixed (TResult* pointer = recording.DirectDestination) {
            var data = new DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> {
                UserData = userData,
                Recorder = recorder,
                Output = recording.GetOutput(pointer, &index)
            };

            host.ForSlice(data,
                static (Entity entity, in DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(entity, data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out data.Output.Next());
                }, recording.Runner, barrier: null);
        }
    }

    #region CurrentThreadRunner

    public static void RecordSlices<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance, barrier: null);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public static void RecordSlicesOnParallel<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorderWithEntity<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default, barrier: null);

    #endregion // ParallelRunner

    #endregion // RecordSlices_WithEntity
}
