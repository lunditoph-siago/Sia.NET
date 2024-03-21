namespace Sia;

public interface IConstructable<TComponent, TTemplate>
{
    static abstract void Construct(TTemplate template, out TComponent component);
}