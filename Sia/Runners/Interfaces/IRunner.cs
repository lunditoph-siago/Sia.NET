namespace Sia;

public interface IRunner : IDisposable
{
    IRunnerBarrier Run(Action action);
    IRunnerBarrier Run<TData>(in TData data, InAction<TData> action);
    IRunnerBarrier Run(int taskCount, GroupAction action);
    IRunnerBarrier Run<TData>(int taskCount, in TData data, GroupAction<TData> action);
}