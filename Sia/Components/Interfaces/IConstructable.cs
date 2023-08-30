namespace Sia;

public interface IConstructable<TTemplate>
{
    void Construct(TTemplate template);
}