namespace Sia;

public interface ISortableCommand<TTarget> : ICommand<TTarget>
    where TTarget : notnull
{
    int Priority { get; }
}

public interface ISortableCommand : ISortableCommand<EntityRef>
{
}