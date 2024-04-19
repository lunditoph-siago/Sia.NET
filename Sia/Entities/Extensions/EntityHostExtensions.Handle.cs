namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    private readonly record struct HandleData(
        IEntityHost Host, EntityHostRangeHandler Handler);
    private readonly record struct HandleData<TData>(
        IEntityHost Host, TData UserData, EntityHostRangeHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner>(
        this IEntityHost host, EntityHostRangeHandler handler, TRunner runner, RunnerBarrier barrier)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) {
            return;
        }
        runner.Run(count, new(host, handler),
            static (in HandleData data, (int, int) range) =>
                data.Handler(data.Host, range.Item1, range.Item2),
            barrier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner>(
        this IEntityHost host, EntityHostRangeHandler handler, TRunner runner)
        where TRunner : IRunner
    {
        var barrier = RunnerBarrier.Get();
        host.Handle(handler, runner, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TData>(
        this IEntityHost host, in TData userData, EntityHostRangeHandler<TData> handler,
        TRunner runner, RunnerBarrier barrier)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) {
            return;
        }
        runner.Run(count, new(host, userData, handler),
            static (in HandleData<TData> data, (int, int) range) =>
                data.Handler(data.Host, data.UserData, range.Item1, range.Item2),
            barrier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TData>(
        this IEntityHost host, in TData userData, EntityHostRangeHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
    {
        var barrier = RunnerBarrier.Get();
        host.Handle(userData, handler, runner, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel(
        this IEntityHost host, EntityHostRangeHandler handler)
        => host.Handle(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel<TData>(
        this IEntityHost host, in TData data, EntityHostRangeHandler<TData> handler)
        => host.Handle(data, handler, ParallelRunner.Default);
}