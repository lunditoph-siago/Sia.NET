namespace Sia_Examples;

public readonly record struct ExampleAppState(int SelectedIndex)
{
    public static ExampleAppState Initial => new(-1);
}
