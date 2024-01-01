namespace Sia;

public interface IPropertyCommand<TValue> : ICommand
{
    static abstract string PropertyName { get; }
    TValue Value { get; }
}