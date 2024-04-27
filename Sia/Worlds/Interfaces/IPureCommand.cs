namespace Sia;

public interface IPureCommand<TComponent> : ICommand<TComponent>
{
    void Execute(ref TComponent component);
}