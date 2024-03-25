namespace Sia;

public interface IBundle
{
    void ToHList(IGenericHandler<IHList> handler);
}