namespace Sia;

public interface IValueComponent<TValue>
{
    TValue Value { get; set; }
}