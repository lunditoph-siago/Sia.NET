namespace Sia;

public interface IParallelCommand : ICommand
{
    void ExecuteOnParallel(in EntityRef target);
}

public interface IParallelCommand<TComponent> : IParallelCommand, ICommand<TComponent>
{
    void ExecuteOnParallel(ref TComponent component);
}