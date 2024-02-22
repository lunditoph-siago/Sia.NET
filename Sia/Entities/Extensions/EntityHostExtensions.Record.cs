namespace Sia;

using CommunityToolkit.HighPerformance.Buffers;

using static EntityExtensionsCommon;

public static partial class EntityHostExtensions
{
    #region EntityRecorder

    public unsafe static SpanOwner<TResult> Record<TRunner, TResult>(
        this IEntityHost host, EntityRecorder<TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new EntityRecordData<TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.Handle(data,
            static (IEntityHost host, in EntityRecordData<TResult> data, int from, int to) => {
                var slots = host.AllocatedSlots;
                var span = data.Array.AsSpan();
                var recorder = data.Recorder;
                ref var index = ref *data.Index;

                for (int i = from; i != to; ++i) {
                    recorder(new(slots[i], host),
                        out span[Interlocked.Increment(ref index)]);
                }
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, TResult>(
        this IEntityHost host, in TData userData, EntityRecorder<TData, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new EntityRecordData<TData, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.Handle(data,
            static (IEntityHost host, in EntityRecordData<TData, TResult> data, int from, int to) => {
                var slots = host.AllocatedSlots;
                var span = data.Array.AsSpan();
                var recorder = data.Recorder;
                ref readonly var userData = ref data.UserData;
                ref var index = ref *data.Index;

                for (int i = from; i != to; ++i) {
                    recorder(new(slots[i], host), userData,
                        out span[Interlocked.Increment(ref index)]);
                }
            }, runner);
        
        return mem;
    }

    private static void IdEntityRecorder(in EntityRef entity, out EntityRef result)
        => result = entity;

    public unsafe static SpanOwner<EntityRef> Record<TRunner>(
        this IEntityHost host, TRunner runner)
        where TRunner : IRunner
        => host.Record<TRunner, EntityRef>(IdEntityRecorder, runner);

    #region CurrentThreadRunner

    public unsafe static SpanOwner<TResult> Record<TResult>(
        this IEntityHost host, EntityRecorder<TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, TResult>(
        this IEntityHost host, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<EntityRef> Record(this IEntityHost host)
        => host.Record<CurrentThreadRunner, EntityRef>(IdEntityRecorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner
    
    #region ParallelRunner

    public unsafe static SpanOwner<TResult> RecordOnParallel<TResult>(
        this IEntityHost host, EntityRecorder<TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, TResult>(
        this IEntityHost host, in TData userData, EntityRecorder<TData, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<EntityRef> RecordOnParallel(this IEntityHost host)
        => host.Record<ParallelRunner, EntityRef>(IdEntityRecorder, ParallelRunner.Default);

    #endregion // ParallelRunner

    #endregion // EntityRecorder

    #region ComponentRecorder

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, TResult>(
        this IEntityHost host, ComponentRecorder<C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, TResult> data, ref C1 c1) => {
                data.Recorder(ref c1,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, C2, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, C2, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                data.Recorder(ref c1, ref c2,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, C2, C3, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, C2, C3, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                data.Recorder(ref c1, ref c2, ref c3,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, C2, C3, C4, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, C2, C3, C4, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                data.Recorder(ref c1, ref c2, ref c3, ref c4,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, C2, C3, C4, C5, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new CompRecordData<C1, C2, C3, C4, C5, C6, TResult> {
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in CompRecordData<C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                data.Recorder(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, TResult> data, ref C1 c1) => {
                data.Recorder(data.UserData,
                    ref c1,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, C2, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, C2, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, C2, TResult> data, ref C1 c1, ref C2 c2) => {
                data.Recorder(data.UserData,
                    ref c1, ref c2,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, C2, C3, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, C2, C3, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, C2, C3, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3) => {
                data.Recorder(data.UserData,
                    ref c1, ref c2, ref c3,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, C2, C3, C4, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, C2, C3, C4, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => {
                data.Recorder(data.UserData,
                    ref c1, ref c2, ref c3, ref c4,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => {
                data.Recorder(data.UserData,
                    ref c1, ref c2, ref c3, ref c4, ref c5,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    public unsafe static SpanOwner<TResult> Record<TRunner, TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder, TRunner runner)
        where TRunner : IRunner
    {
        int count = host.Count;
        if (count == 0) {
            return SpanOwner<TResult>.Empty;
        }

        var mem = SpanOwner<TResult>.Allocate(count);
        int index = -1;

        var data = new DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> {
            UserData = userData,
            Recorder = recorder,
            Array = mem.DangerousGetArray(),
            Index = &index
        };

        host.ForSlice(data,
            static (in DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult> data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => {
                data.Recorder(data.UserData,
                    ref c1, ref c2, ref c3, ref c4, ref c5, ref c6,
                    out data.Array.AsSpan()[Interlocked.Increment(ref *data.Index)]);
            }, runner);
        
        return mem;
    }

    #region CurrentThreadRunner

    public unsafe static SpanOwner<TResult> Record<C1, TResult>(
        this IEntityHost host, ComponentRecorder<C1, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<C1, C2, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<C1, C2, C3, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<C1, C2, C3, C4, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.Record(recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, C2, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, C2, C3, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    public unsafe static SpanOwner<TResult> Record<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.Record(userData, recorder, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, TResult>(
        this IEntityHost host, ComponentRecorder<C1, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, C2, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, C2, C3, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, C2, C3, C4, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.Record(recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, C2, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, C2, C3, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, C2, C3, C4, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, C2, C3, C4, C5, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);

    public unsafe static SpanOwner<TResult> RecordOnParallel<TData, C1, C2, C3, C4, C5, C6, TResult>(
        this IEntityHost host, in TData userData, DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> recorder)
        => host.Record(userData, recorder, ParallelRunner.Default);
    #endregion // ParallelRunner

    #endregion // ComponentRecorder
}