namespace Sia;

public interface IRunner : IDisposable
{
    void Run(Action action);
    void Run(int taskCount, GroupAction action);
    void Run<TData>(int taskCount, in TData data, GroupAction<TData> action);
}