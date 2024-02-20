namespace Sia;

using System.Runtime.CompilerServices;

public sealed class CurrentThreadRunner : IRunner
{
    public static readonly CurrentThreadRunner Instance = new();

    private CurrentThreadRunner() {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run(Action action) => action();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run(int taskCount, GroupAction action) => action((0, taskCount));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run<TData>(int taskCount, in TData data, GroupAction<TData> action) => action(data, (0, taskCount));

    public void Dispose() {}
}