namespace Sia;

public interface IReconstructableCommand<TCommand> : ICommand
{
    static abstract TCommand ReconstructFromCurrentState(in EntityRef entity);
}