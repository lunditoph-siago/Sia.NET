namespace Sia;

using System.Runtime.CompilerServices;
using static EntityExtensionsCommon;

public static partial class EntityHostExtensions
{
    private readonly record struct HandleData(
        IEntityHost Host, EntityHostRangeHandler Handler);
    private readonly record struct HandleData<TData>(
        IEntityHost Host, TData UserData, EntityHostRangeHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle<TRunner>(
        this IEntityHost host, EntityHostRangeHandler handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) {
            return;
        }
        runner.Run(count, new(host, handler),
            static (in HandleData data, (int, int) range) => {
                var host = data.Host;
                int version = host.Version;
                data.Handler(host, range.Item1, range.Item2);
                GuardVersion(version, host.Version);
            },
            barrier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle<TRunner, TData>(
        this IEntityHost host, in TData userData, EntityHostRangeHandler<TData> handler,
        TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) {
            return;
        }
        runner.Run(count, new(host, userData, handler),
            static (in HandleData<TData> data, (int, int) range) => {
                var host = data.Host;
                int version = host.Version;
                data.Handler(host, data.UserData, range.Item1, range.Item2);
                GuardVersion(version, host.Version);
            },
            barrier);
    }

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle(
        this IEntityHost host, EntityHostRangeHandler handler)
        => host.Handle(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle<TData>(
        this IEntityHost host, in TData data, EntityHostRangeHandler<TData> handler)
        => host.Handle(data, handler, CurrentThreadRunner.Instance, barrier: null);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void HandleOnParallel(
        this IEntityHost host, EntityHostRangeHandler handler)
    {
        var barrier = RunnerBarrier.Get();
        host.Handle(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void HandleOnParallel<TData>(
        this IEntityHost host, in TData data, EntityHostRangeHandler<TData> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.Handle(data, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }
    
    #endregion // ParallelRunner
}