namespace Sia_Examples.Editor;

public enum ScrollYStrategy
{
    Nearest,
    Start,
    End,
    Center,
}

public readonly record struct EditorScrollTarget(int Position, ScrollYStrategy Y = ScrollYStrategy.Nearest);
