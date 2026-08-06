namespace Sia_Examples.Editor;

public readonly record struct ViewportState(int From, int To)
{
    public static ViewportState Initial(int visibleLines, int totalLines)
    {
        var count = Math.Max(1, Math.Min(visibleLines, Math.Max(1, totalLines)));
        return new ViewportState(1, count);
    }

    public static ViewportState Compute(ViewportState prev, int cursorLine, int totalLines, int visibleLines)
    {
        totalLines = Math.Max(1, totalLines);
        var count = Math.Max(1, Math.Min(visibleLines, totalLines));
        var maxFrom = Math.Max(1, totalLines - count + 1);
        var from = Math.Clamp(prev.From, 1, maxFrom);

        if (cursorLine < from) from = cursorLine;
        else if (cursorLine > from + count - 1) from = cursorLine - count + 1;

        from = Math.Clamp(from, 1, maxFrom);
        var to = Math.Min(totalLines, from + count - 1);
        return new ViewportState(from, to);
    }
}

public sealed class ViewportTracker(int visibleLines)
{
    public int VisibleLines { get; set; } = visibleLines;

    private ViewportState _state;
    private bool _initialized;

    public ViewportState Current => _state;

    public ViewportState Advance(int cursorLine, int totalLines)
    {
        _state = !_initialized
            ? ViewportState.Initial(VisibleLines, totalLines)
            : ViewportState.Compute(_state, cursorLine, totalLines, VisibleLines);
        _initialized = true;
        return _state;
    }
}
