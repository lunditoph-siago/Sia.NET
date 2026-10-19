namespace Sia.Examples.Editor;

public readonly record struct Range<T>(int From, int To, T Value);
