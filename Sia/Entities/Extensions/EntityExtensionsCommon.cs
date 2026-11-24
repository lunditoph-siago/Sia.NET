using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

internal static class EntityExtensionsCommon
{
    public readonly struct RecordData<TRecorder, TResult>(TRecorder recorder, RecordingOutput<TResult> output)
    {
        public readonly TRecorder Recorder = recorder;
        public readonly RecordingOutput<TResult> Output = output;
    }

    public readonly struct RecordData<TData, TRecorder, TResult>(
        in TData userData, TRecorder recorder, RecordingOutput<TResult> output)
    {
        public readonly TData UserData = userData;
        public readonly TRecorder Recorder = recorder;
        public readonly RecordingOutput<TResult> Output = output;
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
