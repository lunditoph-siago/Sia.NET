namespace Sia;

public interface IGeneratedByTemplate<TComponent, in TTemplate>
    : IConstructable<TComponent, TTemplate>
{
    static abstract void HandleCommandTypes(IGenericTypeHandler<ICommand<TComponent>> handler);
}