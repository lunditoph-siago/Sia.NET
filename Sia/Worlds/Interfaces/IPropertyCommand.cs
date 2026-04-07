namespace Sia;

public interface IPropertyCommand<out TValue> : ICommand
{
    static abstract string PropertyName { get; }
    TValue Value { get; }
}