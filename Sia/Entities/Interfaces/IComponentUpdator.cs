namespace Sia;

public interface IComponentHandler<C1>
{
    void Update(ref C1 c1);
}

public interface IComponentHandler<C1, C2>
{
    void Update(ref C1 c1, ref C2 c2);
}