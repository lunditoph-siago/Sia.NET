namespace Sia;

public interface IExecutable<in TTarget> : ICommand
    where TTarget : notnull
{
    void Execute(TTarget target);
}

public interface IExecutable : IExecutable<EntityRef>
{
}