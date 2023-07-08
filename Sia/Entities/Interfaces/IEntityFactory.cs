namespace Sia;

public interface IEntityFactory
{
    EntityRef Create();
}

public interface IEntityFactory<T> : IEntityFactory
    where T : struct
{
    EntityRef Create(in T initial);
}