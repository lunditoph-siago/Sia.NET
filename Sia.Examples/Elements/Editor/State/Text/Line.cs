namespace Sia_Examples.Editor;

public readonly record struct Line(int From, int To, int Number, string Text, int BreakLength = 0)
{
    public int Length => To - From;
    public int EndIncludingLineBreak => To + BreakLength;
}
