using Microsoft.CodeAnalysis.Text;

namespace Sia.Examples.Editor;

public sealed class EditorViewState(EditorHeightMap heightMap, EditorHeightOracle oracle, Text? document = null)
{
    private Text _document = document ?? oracle.Doc;
    private const double k_Margin = 1000;
    private const double k_MinCoverMargin = 10;
    private const double k_MaxCoverMargin = k_Margin / 4;

    public EditorHeightMap HeightMap { get; private set; } = heightMap;

    public EditorHeightOracle Oracle { get; private set; } = oracle;

    public EditorViewport MainViewport { get; private set; }

    public double PixelViewportTop { get; private set; }

    public double PixelViewportBottom { get; private set; }

    public double DocHeight => HeightMap.Height;

    public void SetHeightMap(EditorHeightMap map) => HeightMap = map;

    public void SetOracle(EditorHeightOracle newOracle) => Oracle = newOracle;

    internal static EditorViewState Create(Text source, bool lineWrapping)
    {
        var oracle = new EditorHeightOracle(lineWrapping).SetDoc(source.Layout);
        var map = EditorHeightMap.Empty().ApplyChanges(Text.Empty, oracle, [(0, 0, 0, source.Layout.Length)]);
        return new(map, oracle, source);
    }

    internal void ApplyChanges(EditorLineUpdate update, Text source)
    {
        var before = update.BeforeDocument.Layout;
        var after = source.Layout;
        var edits = after.ChangesFrom(before);
        if (edits.Count == 1 && edits[0].Span.Start == 0 && edits[0].Span.Length == before.Length) {
            var difference = TextDiff.Find(before.SliceDoc(), after.SliceDoc(), 0);
            edits = difference is { } change ? [new TextChange(TextSpan.FromBounds(change.From, change.To), change.Insert)] : [];
        }
        var ranges = new List<(int FromA, int ToA, int FromB, int ToB)>();
        var delta = 0;
        foreach (var edit in edits) {
            var length = edit.NewText?.Length ?? 0;
            ranges.Add((edit.Span.Start, edit.Span.End, edit.Span.Start + delta, edit.Span.Start + delta + length));
            delta += length - edit.Span.Length;
        }
        var oracle = SetDocument(source);
        if (ranges.Count > 0) HeightMap = HeightMap.ApplyChanges(before, oracle, ranges);
    }

    internal void UpdateMeasuredHeights(int firstLineIndex, double[]? heights, bool force)
    {
        var measured = heights is { Length: > 0 } && firstLineIndex >= 0 && firstLineIndex < _document.Lines
            ? new EditorMeasuredHeights(_document.Layout.Line(firstLineIndex + 1).From, heights) : null;
        HeightMap = HeightMap.UpdateHeight(Oracle, 0, force, measured);
    }

    internal EditorHeightOracle SetDocument(Text source)
    {
        _document = source;
        return Oracle.SetDoc(source.Layout);
    }

    internal EditorBlockInfo LineAt(double value, QueryType type)
    {
        var position = type == QueryType.ByHeight ? value : _document.ToLayoutPosition((int)value);
        var block = HeightMap.LineAt(position, type, Oracle, 0, 0);
        var from = _document.FromLayoutPosition(block.From);
        return new(from, _document.FromLayoutPosition(block.To) - from, block.Top, block.Height);
    }

    public void SetMainViewport(EditorViewport viewport) => MainViewport = viewport;

    public EditorViewport ComputeViewport(double visibleTop, double visibleBottom, double bias = 0)
    {
        PixelViewportTop = visibleTop;
        PixelViewportBottom = visibleBottom;

        var marginTop = 0.5 - System.Math.Max(-0.5, System.Math.Min(0.5, bias / k_Margin / 2));
        var from = LineAt(visibleTop - marginTop * k_Margin, QueryType.ByHeight).From;
        var to = LineAt(visibleBottom + (1 - marginTop) * k_Margin, QueryType.ByHeight).To;
        MainViewport = new EditorViewport(from, to).Clamp(_document.Length);
        return MainViewport;
    }

    public EditorViewport EnsureIncludes(EditorScrollTarget target, double editorPixelHeight)
    {
        if (MainViewport.Contains(target.Position)) {
            return MainViewport;
        }
        var viewHeight = System.Math.Min(editorPixelHeight, PixelViewportBottom - PixelViewportTop);
        var block = LineAt(target.Position, QueryType.ByPos);
        var topPos = target.Y switch {
            ScrollYStrategy.Center => (block.Top + block.Bottom) / 2 - viewHeight / 2,
            ScrollYStrategy.Start => block.Top,
            ScrollYStrategy.Nearest when target.Position < MainViewport.From => block.Top,
            _ => block.Bottom - viewHeight,
        };
        var from = LineAt(topPos - k_Margin / 2, QueryType.ByHeight).From;
        var to = LineAt(topPos + viewHeight + k_Margin / 2, QueryType.ByHeight).To;
        MainViewport = new EditorViewport(from, to).Clamp(_document.Length);
        return MainViewport;
    }

    public EditorViewport MapViewport(EditorViewport viewport, ChangeDesc changes)
    {
        var from = changes.MapPos(viewport.From, -1) ?? viewport.From;
        var to = changes.MapPos(viewport.To, 1) ?? viewport.To;
        return new EditorViewport(
            LineAt(from, QueryType.ByPos).From,
            LineAt(to, QueryType.ByPos).To);
    }

    public bool IsAppropriate(EditorViewport viewport, double bias = 0)
    {
        var top = LineAt(viewport.From, QueryType.ByPos).Top;
        var bottom = LineAt(viewport.To, QueryType.ByPos).Bottom;
        return (viewport.From == 0
                || top <= PixelViewportTop - System.Math.Max(k_MinCoverMargin, System.Math.Min(-bias, k_MaxCoverMargin)))
            && (viewport.To == _document.Length
                || bottom >= PixelViewportBottom + System.Math.Max(k_MinCoverMargin, System.Math.Min(bias, k_MaxCoverMargin)))
            && top > PixelViewportTop - 2 * k_Margin
            && bottom < PixelViewportBottom + 2 * k_Margin;
    }
}
