namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityQueryExtensions
{
    public readonly record struct HandleData(
        IEntityQuery Query, EntityHostRangeHandler Handler);
    public readonly record struct HandleData<TUserData>(
        IEntityQuery Query, TUserData UserData, EntityHostRangeHandler<TUserData> Handler);

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
        this IEntityQuery query, EntityHostRangeHandler handler, TRunner runner)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) { return; }

        runner.Run<HandleData>(count, new(query, handler), static (data, range) => {
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
        });
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TUserData>(
        this IEntityQuery query, in TUserData userData, EntityHostRangeHandler<TUserData> handler,
        TRunner runner)
        where TRunner : IRunner
    {
        var count = query.Count;
        if (count == 0) { return; }

        var data = new HandleData<TUserData>(query, userData, handler);
        runner.Run(count, data, static (data, range) => {
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
        });
    }

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle(
        this IEntityQuery query, EntityHostRangeHandler handler)
        => query.Handle(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TUserData>(
        this IEntityQuery query, in TUserData data, EntityHostRangeHandler<TUserData> handler)
        => query.Handle(data, handler, CurrentThreadRunner.Instance);

    #endregion

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel(
        this IEntityQuery query, EntityHostRangeHandler handler)
        => query.Handle(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel<TUserData>(
        this IEntityQuery query, in TUserData data, EntityHostRangeHandler<TUserData> handler)
        => query.Handle(data, handler, ParallelRunner.Default);
    
    #endregion
}