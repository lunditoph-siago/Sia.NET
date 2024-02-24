#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using static EntityExtensionsCommon;

public static partial class EntityHostExtensions
{
    #region EntityRecorder

    public unsafe static void Record<TRunner, TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new EntityRecordData<TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.Handle(data,
                static (IEntityHost host, in EntityRecordData<TResult> data, int from, int to) => {
                    var slots = host.AllocatedSlots;
                    var pointer = data.Pointer;
                    var recorder = data.Recorder;
                    ref var index = ref *data.Index;

                    for (int i = from; i != to; ++i) {
                        recorder(new(slots[i], host),
                            out *(pointer + Interlocked.Increment(ref index)));
                    }
                }, runner);
        }
    }

    public unsafe static void Record<TRunner, TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new EntityRecordData<TData, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.Handle(data,
                static (IEntityHost host, in EntityRecordData<TData, TResult> data, int from, int to) => {
                    var slots = host.AllocatedSlots;
                    var pointer = data.Pointer;
                    var recorder = data.Recorder;
                    ref readonly var userData = ref data.UserData;
                    ref var index = ref *data.Index;

                    for (int i = from; i != to; ++i) {
                        recorder(userData, new(slots[i], host),
                            out *(pointer + Interlocked.Increment(ref index)));
                    }
                }, runner);
        }
    }

    private static void IdEntityRecorder(in EntityRef entity, out EntityRef result)
        => result = entity;

    public unsafe static void Record<TRunner>(
        this IEntityHost host, Span<EntityRef> span, TRunner runner)
        where TRunner : IRunner
        => host.Record(span, IdEntityRecorder, runner);

    #region CurrentThreadRunner

    public unsafe static void Record<TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder)
        => host.Record(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void Record<TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void Record(
        this IEntityHost host, Span<EntityRef> span)
        => host.Record(span, IdEntityRecorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner
    
    #region ParallelRunner

    public unsafe static void RecordOnParallel<TResult>(
        this IEntityHost host, Span<TResult> span, EntityRecorder<TResult> recorder)
        => host.Record(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordOnParallel<TData, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordOnParallel(
        this IEntityHost host, Span<EntityRef> span)
        => host.Record(span, IdEntityRecorder, ParallelRunner.Default);

    #endregion // ParallelRunner

    #endregion // EntityRecorder

    #region ComponentRecorder

    public unsafe static void RecordSlices<TRunner, C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, TResult> data, ref C1 c1) => {
                    data.Recorder(ref c1,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(ref c1, ref c2,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(ref c1, ref c2, ref c3,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, C6, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, TResult> data, ref C1 c1) => {
                    data.Recorder(data.UserData,
                        ref c1,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public unsafe static void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        GuardSpanLength(span, host.Count);
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            host.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    #region CurrentThreadRunner

    public unsafe static void RecordSlices<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static void RecordSlices<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public unsafe static void RecordSlicesOnParallel<C1, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, C2, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, C2, C3, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public unsafe static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.RecordSlices(span, userData, recorder, ParallelRunner.Default);
    #endregion // ParallelRunner

    #endregion // ComponentRecorder
}

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type