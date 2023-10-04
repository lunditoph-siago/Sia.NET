namespace Sia;

public sealed class SystemHandle : IDisposable
{
    public ISystem System { get; }
    public Scheduler.TaskGraphNode TaskGraphNode { get; }

    private readonly Action<SystemHandle> _onDispose;
    private bool _disposed;

    internal SystemHandle(
        ISystem system, Scheduler.TaskGraphNode taskGraphNode, Action<SystemHandle> onDispose)
    {
        System = system;
        TaskGraphNode = taskGraphNode;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _onDispose(this);
        _disposed = true;
    }
}