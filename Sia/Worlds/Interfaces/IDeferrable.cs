namespace Sia;

public interface IDeferrable<in TTarget> : IExecutable<TTarget>
    where TTarget : notnull
{
    bool ShouldDefer(TTarget target);
}

public interface IDeferrable : IDeferrable<EntityRef>, IExecutable
{
}