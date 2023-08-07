namespace Sia;

public abstract class SetCommand<TCommand, TComponent, TValue>
    : PropertyCommand<TCommand, TValue>
    where TCommand : ICommand<EntityRef>, new()
    where TComponent : struct, IValueComponent<TValue>
{
    public override void Execute(in EntityRef target)
        => target.Get<TComponent>().Value = Value!;
}