namespace Sia;

using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    public readonly record struct HandleData(
        IEntityHost Host, EntityHostRangeHandler Handler);
    public readonly record struct HandleData<TUserData>(
        IEntityHost Host, TUserData UserData, EntityHostRangeHandler<TUserData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner>(
        this IEntityHost host, EntityHostRangeHandler handler, TRunner runner)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) { return; }

        runner.Run(count, new(host, handler), static (in HandleData data, (int, int) range) => {
            data.Handler(data.Host, range.Item1, range.Item2);
        });
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Handle<TRunner, TUserData>(
        this IEntityHost host, in TUserData userData, EntityHostRangeHandler<TUserData> handler,
        TRunner runner)
        where TRunner : IRunner
    {
        var count = host.Count;
        if (count == 0) { return; }

        runner.Run(count, new(host, userData, handler), static (in HandleData<TUserData> data, (int, int) range) => {
            data.Handler(data.Host, data.UserData, range.Item1, range.Item2);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel(
        this IEntityHost host, EntityHostRangeHandler handler)
        => host.Handle(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void HandleOnParallel<TUserData>(
        this IEntityHost host, in TUserData data, EntityHostRangeHandler<TUserData> handler)
        => host.Handle(data, handler, ParallelRunner.Default);
}