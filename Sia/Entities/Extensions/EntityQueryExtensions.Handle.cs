namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static partial class EntityQueryExtensions
{
    public readonly record struct HandleData(
        IEntityQuery Query, EntityHostRangeHandler Handler);
    public readonly record struct HandleData<TData>(
        IEntityQuery Query, TData UserData, EntityHostRangeHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (IEntityHost Host, int HostIndex, int SlotIndex) FindHost(
        IReadOnlyList<IEntityHost> hosts, int startIndex)
    {
        IEntityHost host;

        int hostIndex = -1;
        int counter = 0;
        int prevCounter;

        do {
            host = hosts[++hostIndex];
            prevCounter = counter;
            counter += host.Count;
        } while (counter < startIndex);

        return (host, hostIndex, startIndex - prevCounter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner>(
        this IEntityQuery query, EntityHostRangeHandler handler, TRunner runner, out IRunnerBarrier barrier)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) {
            barrier = RunnerBarriers.Empty;
            return;
        }
        
        static void Action(in HandleData data, (int, int) range)
        {
            var (from, to) = range;
            var remainingCount = to - from;

            var handler = data.Handler;
            var hosts = data.Query.Hosts;
            var (host, hostIndex, slotIndex) = FindHost(hosts, from);

            while (true) {
                var slotCount = host.Count;
                remainingCount -= slotCount;

                if (remainingCount <= slotCount) {
                    handler(host, slotIndex, remainingCount);
                    return;
                }
                else {
                    handler(host, slotIndex, slotCount);
                    host = hosts[++hostIndex];
                    slotIndex = 0;
                }
            }
        }

        barrier = runner.Run(count, new HandleData(query, handler), Action);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner>(
        this IEntityQuery query, EntityHostRangeHandler handler, TRunner runner)
        where TRunner : IRunner
    {
        query.Handle(handler, runner, out var barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TData>(
        this IEntityQuery query, in TData userData, EntityHostRangeHandler<TData> handler,
        TRunner runner, out IRunnerBarrier barrier)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) {
            barrier = RunnerBarriers.Empty;
            return;
        }

        static void Action(in HandleData<TData> data, (int, int) range)
        {
            var (from, to) = range;
            var remainingCount = to - from;

            var handler = data.Handler;
            var hosts = data.Query.Hosts;
            var (host, hostIndex, slotIndex) = FindHost(hosts, from);

            while (true) {
                var slotCount = host.Count;
                if (remainingCount < slotCount) {
                    handler(host, data.UserData, slotIndex, slotIndex + remainingCount);
                    return;
                }

                handler(host, data.UserData, slotIndex, slotCount);
                remainingCount -= slotCount;

                if (remainingCount == 0) {
                    return;
                }
                host = hosts[++hostIndex];
                slotIndex = 0;
            }
        };

        barrier = runner.Run(
            count, new HandleData<TData>(query, userData, handler), Action);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TData>(
        this IEntityQuery query, in TData userData, EntityHostRangeHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
    {
        query.Handle(userData, handler, runner, out var barrier);
        barrier.WaitAndReturn();
    }

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle(
        this IEntityQuery query, EntityHostRangeHandler handler)
        => query.Handle(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TData>(
        this IEntityQuery query, in TData data, EntityHostRangeHandler<TData> handler)
        => query.Handle(data, handler, CurrentThreadRunner.Instance);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel(
        this IEntityQuery query, EntityHostRangeHandler handler)
        => query.Handle(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel<TData>(
        this IEntityQuery query, in TData data, EntityHostRangeHandler<TData> handler)
        => query.Handle(data, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner
}