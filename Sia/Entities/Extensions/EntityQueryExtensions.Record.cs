#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using static EntityExtensionsCommon;

public static partial class EntityQueryExtensions
{
    #region EntityRecorder

    public static unsafe void Record<TRunner, TResult>(
        this IEntityQuery query, Span<TResult> span, EntityRecorder<TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new EntityRecordData<TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.Handle(data,
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

    public static unsafe void Record<TRunner, TData, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new EntityRecordData<TData, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.Handle(data,
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

    public static void Record<TRunner>(
        this IEntityQuery query, Span<EntityRef> span, TRunner runner)
        where TRunner : IRunner
        => query.Record(span, IdEntityRecorder, runner);

    #region CurrentThreadRunner

    public static void Record<TResult>(
        this IEntityQuery query, Span<TResult> span, EntityRecorder<TResult> recorder)
        => query.Record(span, recorder, CurrentThreadRunner.Instance);

    public static void Record<TData, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => query.Record(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void Record(
        this IEntityQuery query, Span<EntityRef> span)
        => query.Record(span, IdEntityRecorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner
    
    #region ParallelRunner

    public static void RecordOnParallel<TResult>(
        this IEntityQuery query, Span<TResult> span, EntityRecorder<TResult> recorder)
        => query.Record(span, recorder, ParallelRunner.Default);

    public static void RecordOnParallel<TData, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, EntityRecorder<TData, TResult> recorder)
        => query.Record(span, userData, recorder, ParallelRunner.Default);

    public static void RecordOnParallel(
        this IEntityQuery query, Span<EntityRef> span)
        => query.Record(span, IdEntityRecorder, ParallelRunner.Default);

    #endregion // ParallelRunner

    #endregion // EntityRecorder

    #region ComponentRecorder

    public static unsafe void RecordSlices<TRunner, C1, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, TResult> data, ref C1 c1) => {
                    data.Recorder(ref c1,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(ref c1, ref c2,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(ref c1, ref c2, ref c3,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new CompRecordData<C1, C2, C3, C4, C5, C6, TResult> {
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in CompRecordData<C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, TResult> data, ref C1 c1) => {
                    data.Recorder(data.UserData,
                        ref c1,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    public static unsafe void RecordSlices<TRunner, TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int index = -1;

        fixed (TResult* pointer = span) {
            var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> {
                UserData = userData,
                Recorder = recorder,
                Pointer = pointer,
                Index = &index
            };

            query.ForSlice(data,
                static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                    data.Recorder(data.UserData,
                        ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                        out *(data.Pointer + Interlocked.Increment(ref *data.Index)));
                }, runner);
        }
    }

    #region CurrentThreadRunner

    public static void RecordSlices<C1, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => query.RecordSlices(span, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    public static void RecordSlices<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public static void RecordSlicesOnParallel<C1, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => query.RecordSlices(span, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, C2, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);

    public static void RecordSlicesOnParallel<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityQuery query, Span<TResult> span, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => query.RecordSlices(span, userData, recorder, ParallelRunner.Default);
    #endregion // ParallelRunner

    #endregion // ComponentRecorder
}

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type