namespace Sia;

public interface IParallelCommand : ICommand
{
    void ExecuteOnParallel(in EntityRef target);
}