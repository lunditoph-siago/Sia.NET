namespace Sia;

public interface IPropertyCommand<TValue>
{
    static abstract string PropertyName { get; }
    TValue Value { get; }
}