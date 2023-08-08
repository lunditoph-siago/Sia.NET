namespace Sia;

public interface ISingleWorldComponent<TWorld, TTarget>
    where TWorld : World<TTarget>
    where TTarget : notnull
{
    TWorld World { get; }
}

public interface ISingleWorldComponent<TWorld> : ISingleWorldComponent<TWorld, EntityRef>
    where TWorld : World<EntityRef>
{
}