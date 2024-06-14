namespace Sia;

public interface IReconstructableCommand<TCommand> : ICommand
{
    static abstract TCommand ReconstructFromCurrentState(Entity entity);
}