namespace Sia_Examples.Editor;

public sealed class EditorViewState(EditorHeightMap heightMap, EditorHeightOracle oracle)
{
    private const double Margin = 1000;
    private const double MinCoverMargin = 10;
    private const double MaxCoverMargin = Margin / 4;

    public EditorHeightMap HeightMap { get; private set; } = heightMap;

    public EditorHeightOracle Oracle { get; private set; } = oracle;

    public EditorViewport MainViewport { get; private set; }

    public double PixelViewportTop { get; private set; }

    public double PixelViewportBottom { get; private set; }

    public double DocHeight => HeightMap.Height;

    public void SetHeightMap(EditorHeightMap map) => HeightMap = map;

    public void SetOracle(EditorHeightOracle newOracle) => Oracle = newOracle;

    public void SetMainViewport(EditorViewport viewport) => MainViewport = viewport;

    public EditorViewport ComputeViewport(double visibleTop, double visibleBottom, double bias = 0)
    {
        PixelViewportTop = visibleTop;
        PixelViewportBottom = visibleBottom;

        var marginTop = 0.5 - Math.Max(-0.5, Math.Min(0.5, bias / Margin / 2));
        var from = HeightMap.LineAt(visibleTop - marginTop * Margin, QueryType.ByHeight, Oracle, 0, 0).From;
        var to = HeightMap.LineAt(visibleBottom + (1 - marginTop) * Margin, QueryType.ByHeight, Oracle, 0, 0).To;
        MainViewport = new EditorViewport(from, to).Clamp(HeightMap.Length);
        return MainViewport;
    }

    public EditorViewport EnsureIncludes(EditorScrollTarget target, double editorPixelHeight)
    {
        if (MainViewport.Contains(target.Position)) {
            return MainViewport;
        }
        var viewHeight = Math.Min(editorPixelHeight, PixelViewportBottom - PixelViewportTop);
        var block = HeightMap.LineAt(target.Position, QueryType.ByPos, Oracle, 0, 0);
        var topPos = target.Y switch {
            ScrollYStrategy.Center => (block.Top + block.Bottom) / 2 - viewHeight / 2,
            ScrollYStrategy.Start => block.Top,
            ScrollYStrategy.Nearest when target.Position < MainViewport.From => block.Top,
            _ => block.Bottom - viewHeight,
        };
        var from = HeightMap.LineAt(topPos - Margin / 2, QueryType.ByHeight, Oracle, 0, 0).From;
        var to = HeightMap.LineAt(topPos + viewHeight + Margin / 2, QueryType.ByHeight, Oracle, 0, 0).To;
        MainViewport = new EditorViewport(from, to).Clamp(HeightMap.Length);
        return MainViewport;
    }

    public EditorViewport MapViewport(EditorViewport viewport, ChangeDesc changes)
    {
        var from = changes.MapPos(viewport.From, -1) ?? viewport.From;
        var to = changes.MapPos(viewport.To, 1) ?? viewport.To;
        return new EditorViewport(
            HeightMap.LineAt(from, QueryType.ByPos, Oracle, 0, 0).From,
            HeightMap.LineAt(to, QueryType.ByPos, Oracle, 0, 0).To);
    }

    public bool IsAppropriate(EditorViewport viewport, double bias = 0)
    {
        var top = HeightMap.LineAt(viewport.From, QueryType.ByPos, Oracle, 0, 0).Top;
        var bottom = HeightMap.LineAt(viewport.To, QueryType.ByPos, Oracle, 0, 0).Bottom;
        return (viewport.From == 0
                || top <= PixelViewportTop - Math.Max(MinCoverMargin, Math.Min(-bias, MaxCoverMargin)))
            && (viewport.To == HeightMap.Length
                || bottom >= PixelViewportBottom + Math.Max(MinCoverMargin, Math.Min(bias, MaxCoverMargin)))
            && top > PixelViewportTop - 2 * Margin
            && bottom < PixelViewportBottom + 2 * Margin;
    }
}
