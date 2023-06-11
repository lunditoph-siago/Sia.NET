namespace Sia;

public interface ICommand : IDisposable
{
}

public interface IExecutableCommand<TTarget> : ICommand
{
    void Execute(TTarget target);
}