namespace Sia;

public interface IEntityHostProvider
{
    IEntityHost<TEntity> GetHost<TEntity>()
        where TEntity : IHList;
}