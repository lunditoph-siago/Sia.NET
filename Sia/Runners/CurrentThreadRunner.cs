namespace Sia;

using System.Runtime.CompilerServices;

public sealed class CurrentThreadRunner : IRunner
{
    public static readonly CurrentThreadRunner Instance = new();

    private CurrentThreadRunner() {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IRunnerBarrier Run(Action action)
    {
        action();
        return RunnerBarriers.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IRunnerBarrier Run<TData>(in TData data, InAction<TData> action)
    {
        action(data);
        return RunnerBarriers.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IRunnerBarrier Run(int taskCount, GroupAction action)
    {
        action((0, taskCount));
        return RunnerBarriers.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IRunnerBarrier Run<TData>(int taskCount, in TData data, GroupAction<TData> action)
    {
        action(data, (0, taskCount));
        return RunnerBarriers.Empty;
    }

    public void Dispose() {}
}