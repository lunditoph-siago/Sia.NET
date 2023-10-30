namespace Sia;

public interface IParallelCommand : ICommand
{
    void ExecuteOnParallel(in EntityRef target);
}

public interface IParallelCommand<T> : ICommand<T>
{
    void ExecuteOnParallel(ref T component);
}