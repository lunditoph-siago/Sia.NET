namespace Sia_Examples.Editor;

public readonly record struct Line(int From, int To, int Number, string Text)
{
    public int Length => To - From;
}
