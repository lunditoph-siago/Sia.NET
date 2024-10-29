namespace Sia;

using System.Runtime.CompilerServices;
using static EntityExtensionsCommon;

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
    public static void Handle<TRunner>(
        this IEntityQuery query, EntityHostRangeHandler handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) {
            return;
        }
        
        static void Action(in HandleData data, (int, int) range)
        {
            var (from, to) = range;
            var remainingCount = to - from;

            var handler = data.Handler;
            var hosts = data.Query.Hosts;
            var (host, hostIndex, slotIndex) = FindHost(hosts, from);
            int version = host.Version;

            while (true) {
                var slotCount = host.Count;
                remainingCount -= slotCount;

                if (remainingCount <= slotCount) {
                    handler(host, slotIndex, remainingCount);
                    GuardVersion(version, host.Version);
                    return;
                }
                else {
                    handler(host, slotIndex, slotCount);
                    GuardVersion(version, host.Version);
                    host = hosts[++hostIndex];
                    slotIndex = 0;
                }
            }
        }

        runner.Run(count, new HandleData(query, handler), Action, barrier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle<TRunner, TData>(
        this IEntityQuery query, in TData userData, EntityHostRangeHandler<TData> handler,
        TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) {
            return;
        }

        static void Action(in HandleData<TData> data, (int, int) range)
        {
            var (from, to) = range;
            var remainingCount = to - from;

            var handler = data.Handler;
            var hosts = data.Query.Hosts;
            var (host, hostIndex, slotIndex) = FindHost(hosts, from);
            int version = host.Version;

            int slotCount = host.Count - slotIndex;
            if (remainingCount <= slotCount) {
                handler(host, data.UserData, slotIndex, slotIndex + remainingCount);
                GuardVersion(version, host.Version);
                return;
            }
            handler(host, data.UserData, slotIndex, slotIndex + slotCount);
            GuardVersion(version, host.Version);
            remainingCount -= slotCount;
            if (remainingCount == 0) { return; }

            host = hosts[++hostIndex];
            slotCount = host.Count;

            while (true) {
                if (remainingCount <= slotCount) {
                    handler(host, data.UserData, 0, remainingCount);
                    GuardVersion(version, host.Version);
                    return;
                }
                handler(host, data.UserData, 0, slotCount);
                GuardVersion(version, host.Version);
                remainingCount -= slotCount;

                host = hosts[++hostIndex];
                slotCount = host.Count;
            }
        };

        var data = new HandleData<TData>(query, userData, handler);
        runner.Run(count, data, Action, barrier);
    }

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle(
        this IEntityQuery query, EntityHostRangeHandler handler)
        => query.Handle(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle<TData>(
        this IEntityQuery query, in TData data, EntityHostRangeHandler<TData> handler)
        => query.Handle(data, handler, CurrentThreadRunner.Instance, barrier: null);

    #endregion // CurrentThreadRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void HandleOnParallel(
        this IEntityQuery query, EntityHostRangeHandler handler)
    {
        var barrier = RunnerBarrier.Get();
        query.Handle(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void HandleOnParallel<TData>(
        this IEntityQuery query, in TData data, EntityHostRangeHandler<TData> handler)
    {
        var barrier = RunnerBarrier.Get();
        query.Handle(data, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }
    
    #endregion // ParallelRunner
}