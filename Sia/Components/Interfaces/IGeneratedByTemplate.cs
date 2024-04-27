namespace Sia;

public interface IGeneratedByTemplate<TComponent, TTemplate>
    : IConstructable<TComponent, TTemplate>
{
    abstract static void HandleCommandTypes(IGenericTypeHandler<ICommand<TComponent>> handler);
}