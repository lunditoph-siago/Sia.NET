namespace Sia;

public interface IConstructable<TComponent, in TTemplate>
{
    static abstract void Construct(TTemplate template, out TComponent component);
}