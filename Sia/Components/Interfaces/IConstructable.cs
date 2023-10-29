namespace Sia;

public interface IConstructable<TComponent, TTemplate>
{
    abstract static void Construct(TTemplate template, out TComponent result);
}