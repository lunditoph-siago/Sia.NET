namespace Sia.Reactive;

public sealed class HookRef<T>(T value)
{
    public T Value { get; set; } = value;
}
