namespace Sia;

public interface IReconstructableCommand<out TCommand> : ICommand
{
    static abstract TCommand ReconstructFromCurrentState(Entity entity);
}