namespace Sia_Examples.Editor;

public sealed class EditorMeasuredHeights(int from, IReadOnlyList<double> heights)
{
    public int From { get; } = from;

    public IReadOnlyList<double> Heights { get; } = heights;

    public int Index { get; set; }

    public bool More => Index < Heights.Count;
}
