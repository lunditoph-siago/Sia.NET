namespace Sia;

public interface IBundle
{
    void ToHList(IGenericHandler handler);

    void ToHList(IGenericHandler<IHList> handler);
}