using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

internal static class EntityExtensionsCommon
{
    public struct EntityRecordData<TResult>
    {
        public EntityRecorder<TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct EntityRecordData<TData, TResult>
    {
        public TData UserData;
        public EntityRecorder<TData, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, TResult>
    {
        public ComponentRecorder<C1, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, C2, TResult>
    {
        public ComponentRecorder<C1, C2, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, C2, C3, TResult>
    {
        public ComponentRecorder<C1, C2, C3, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, C2, C3, C4, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, C2, C3, C4, C5, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, C5, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordData<C1, C2, C3, C4, C5, C6, TResult>
    {
        public ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, C2, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, C2, C3, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, C2, C3, C4, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, C2, C3, C4, C5, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordData<TData, C1, C2, C3, C4, C5, C6, TResult>
    {
        public TData UserData;
        public DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, TResult>
    {
        public ComponentRecorderWithEntity<C1, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, C2, TResult>
    {
        public ComponentRecorderWithEntity<C1, C2, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, C2, C3, TResult>
    {
        public ComponentRecorderWithEntity<C1, C2, C3, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, C2, C3, C4, TResult>
    {
        public ComponentRecorderWithEntity<C1, C2, C3, C4, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, C2, C3, C4, C5, TResult>
    {
        public ComponentRecorderWithEntity<C1, C2, C3, C4, C5, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct CompRecordDataWithEntity<C1, C2, C3, C4, C5, C6, TResult>
    {
        public ComponentRecorderWithEntity<C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, C2, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, C2, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, C2, C3, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, C2, C3, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    public struct DataCompRecordDataWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult>
    {
        public TData UserData;
        public DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult> Recorder;
        public RecordingOutput<TResult> Output;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GuardSpanLength<TResult>(Span<TResult> span, int requiredSize)
    {
        if (span.Length < requiredSize) {
            throw new ArgumentException("Span length is not sufficient");
        }
    }

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GuardVersion(int currentVersion, int requiredVersion)
    {
        if (currentVersion != requiredVersion) {
            throw new InvalidOperationException("Entity host cannot be modified during the task");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GuardSequentialBytes(IEntityHost host, Span<byte> bytes)
    {
        if (host.TryGetSequentialBytes(out var current)
            && !Unsafe.AreSame(
                ref MemoryMarshal.GetReference(current),
                ref MemoryMarshal.GetReference(bytes))) {
            throw new InvalidOperationException(
                "Entity host storage was reallocated during the iteration");
        }
    }
}
