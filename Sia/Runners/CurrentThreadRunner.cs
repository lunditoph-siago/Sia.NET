namespace Sia;

public sealed class CurrentThreadRunner : IRunner
{
    public static readonly CurrentThreadRunner Instance = new();

    private CurrentThreadRunner() {}

    public void Run(Action action, RunnerBarrier? barrier = null)
        => action();

    public void Run<TData>(in TData data, InAction<TData> action, RunnerBarrier? barrier = null)
        => action(data);

    public void Run(int taskCount, GroupAction action, RunnerBarrier? barrier = null)
        => action((0, taskCount));

    public void Run<TData>(int taskCount, in TData data, GroupAction<TData> action, RunnerBarrier? barrier = null)
        => action(data, (0, taskCount));

    public void Dispose() {}
}