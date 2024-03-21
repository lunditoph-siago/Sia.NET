namespace Sia;

public interface IBundle
{
    void ToMany(IGenericHandler<IHList> handler);
}