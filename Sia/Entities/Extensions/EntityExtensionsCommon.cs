namespace Sia;

internal static class EntityExtensionsCommon
{
    public unsafe struct EntityRecordData<TResult>
    {
        public EntityRecorder<TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct EntityRecordData<TData, TResult>
    {
        public TData UserData;
        public EntityRecorder<TData, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, TResult>
    {
        public ComponentRecorder<C1, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, C2, TResult>
    {
        public ComponentRecorder<C1, C2, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, C2, C3, TResult>
    {
        public ComponentRecorder<C1, C2, C3, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, C2, C3, C4, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, C2, C3, C4, C5, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, C5, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct CompRecordData<C1, C2, C3, C4, C5, C6, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, C2, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, C2, C3, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, C2, C3, C4, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }

    public unsafe struct DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public ArraySegment<TResult> Array;
        public int* Index;
    }
}