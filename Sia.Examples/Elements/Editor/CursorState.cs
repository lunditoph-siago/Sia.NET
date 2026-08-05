namespace Sia_Examples.Editor;

public readonly record struct CursorState(
    int Line,
    int Column,
    int AnchorLine,
    int AnchorColumn,
    int PreferredColumn,
    EditorMode Mode,
    int ScrollLine)
{
    public CursorState() : this(0, 0, -1, -1, 0, EditorMode.Insert, 0) { }

    public bool HasSelection => AnchorLine >= 0;

    public static CursorState Default => new();

    public (int Line, int Col) SelectionStart => HasSelection
        ? PositionIsBefore(AnchorLine, AnchorColumn, Line, Column)
            ? (AnchorLine, AnchorColumn)
            : (Line, Column)
        : (Line, Column);

    public (int Line, int Col) SelectionEnd => HasSelection
        ? PositionIsBefore(AnchorLine, AnchorColumn, Line, Column)
            ? (Line, Column)
            : (AnchorLine, AnchorColumn)
        : (Line, Column);

    private static bool PositionIsBefore(int l1, int c1, int l2, int c2)
        => l1 < l2 || (l1 == l2 && c1 < c2);

    public CursorState WithSelection(int anchorLine, int anchorColumn)
        => this with { AnchorLine = anchorLine, AnchorColumn = anchorColumn };

    public CursorState WithoutSelection()
        => this with { AnchorLine = -1, AnchorColumn = -1 };

    public CursorState WithPosition(int line, int column)
        => this with { Line = line, Column = column };

    public CursorState WithScroll(int scrollLine)
        => this with { ScrollLine = scrollLine };
}
