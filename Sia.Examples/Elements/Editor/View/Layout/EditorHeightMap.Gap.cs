namespace Sia.Examples.Editor;

public sealed class EditorHeightMapGap(int length, bool isSingleLine = false) : EditorHeightMap(length, 0)
{
    public bool IsSingleLine { get; } = isSingleLine;

    private (int FirstLine, int LastLine, double PerLine, double PerChar) HeightMetrics(
        EditorHeightOracle oracle, int offset)
    {
        var firstLine = oracle.Doc.LineAt(offset).Number;
        var lastLine = oracle.Doc.LineAt(offset + Length).Number;
        var lines = lastLine - firstLine + 1;
        double perLine;
        var perChar = 0.0;
        if (oracle.LineWrapping) {
            var totalPerLine = System.Math.Min(Height, oracle.LineHeight * lines);
            perLine = totalPerLine / lines;
            var textLength = Length - lines + 1;
            if (textLength > 0) {
                perChar = (Height - totalPerLine) / textLength;
            }
        }
        else {
            perLine = Height / lines;
        }
        return (firstLine, lastLine, perLine, perChar);
    }

    public override EditorBlockInfo BlockAt(double height, EditorHeightOracle oracle, double top, int offset)
    {
        var (firstLine, lastLine, perLine, perChar) = HeightMetrics(oracle, offset);
        if (oracle.LineWrapping) {
            var low = firstLine;
            var high = lastLine;
            while (low < high) {
                var middle = low + (high - low + 1) / 2;
                var line = oracle.Doc.Line(middle);
                var linesAbove = middle - firstLine;
                var lineHeight = perLine + line.Length * perChar;
                var lineTop = top + perLine * linesAbove + perChar * (line.From - offset - linesAbove);
                lineTop = System.Math.Max(top, System.Math.Min(lineTop, top + Height - lineHeight));
                if (lineTop > height) high = middle - 1;
                else low = middle;
            }
            return LineAt(oracle.Doc.Line(low).From, QueryType.ByPos, oracle, top, offset);
        }
        var lineIndex = System.Math.Max(0, System.Math.Min(lastLine - firstLine, (int)System.Math.Floor((height - top) / perLine)));
        var target = oracle.Doc.Line(firstLine + lineIndex);
        return new EditorBlockInfo(target.From, target.Length, top + perLine * lineIndex, perLine);
    }

    public override EditorBlockInfo LineAt(
        double value, QueryType type, EditorHeightOracle oracle, double top, int offset)
    {
        if (type == QueryType.ByHeight) {
            return BlockAt(value, oracle, top, offset);
        }
        if (type == QueryType.ByPosNoHeight) {
            var noHeight = oracle.Doc.LineAt((int)value);
            return new EditorBlockInfo(noHeight.From, noHeight.Length, 0, 0);
        }
        var (firstLine, _, perLine, perChar) = HeightMetrics(oracle, offset);
        var line = oracle.Doc.LineAt((int)value);
        var lineHeight = perLine + line.Length * perChar;
        var linesAbove = line.Number - firstLine;
        var lineTop = top + perLine * linesAbove + perChar * (line.From - offset - linesAbove);
        return new EditorBlockInfo(
            line.From, line.Length,
            System.Math.Max(top, System.Math.Min(lineTop, top + Height - lineHeight)),
            lineHeight);
    }

    public override void ForEachLine(
        int from, int to, EditorHeightOracle oracle, double top, int offset, Action<EditorBlockInfo> action)
    {
        from = System.Math.Max(from, offset);
        to = System.Math.Min(to, offset + Length);
        var (firstLine, _, perLine, perChar) = HeightMetrics(oracle, offset);
        var lineTop = top;
        for (var pos = from; pos <= to;) {
            var line = oracle.Doc.LineAt(pos);
            if (pos == from) {
                var linesAbove = line.Number - firstLine;
                lineTop += perLine * linesAbove + perChar * (from - offset - linesAbove);
            }
            var lineHeight = perLine + perChar * line.Length;
            action(new EditorBlockInfo(line.From, line.Length, lineTop, lineHeight));
            lineTop += lineHeight;
            pos = line.To + 1;
        }
    }

    public override EditorHeightMap Replace(int from, int to, IReadOnlyList<EditorHeightMap?> nodes)
    {
        var built = new List<EditorHeightMap?>(nodes);
        var after = Length - to;
        if (after > 0) {
            if (built[^1] is EditorHeightMapGap last) {
                built[^1] = new EditorHeightMapGap(last.Length + after);
            }
            else {
                built.Add(null);
                built.Add(new EditorHeightMapGap(after - 1));
            }
        }
        if (from > 0) {
            if (built[0] is EditorHeightMapGap first) {
                built[0] = new EditorHeightMapGap(from + first.Length);
            }
            else {
                built.Insert(0, null);
                built.Insert(0, new EditorHeightMapGap(from - 1));
            }
        }
        return Of(built);
    }

    public override void DecomposeLeft(int to, List<EditorHeightMap?> result)
    {
        result.Add(new EditorHeightMapGap(to - 1));
        result.Add(null);
    }

    public override void DecomposeRight(int from, List<EditorHeightMap?> result)
    {
        result.Add(null);
        result.Add(new EditorHeightMapGap(Length - from - 1));
    }

    public override EditorHeightMap UpdateHeight(
        EditorHeightOracle oracle, int offset = 0, bool force = false, EditorMeasuredHeights? measured = null)
    {
        var end = offset + Length;
        if (measured is not null && measured.From <= offset + Length && measured.More) {
            var nodes = new List<EditorHeightMap?>();
            var pos = System.Math.Max(offset, measured.From);
            double? singleHeight = null;
            var mixedHeights = false;
            if (measured.From > offset) {
                nodes.Add(new EditorHeightMapGap(measured.From - offset - 1).UpdateHeight(oracle, offset));
            }
            while (pos <= end && measured.More) {
                var len = oracle.Doc.LineAt(pos).Length;
                if (nodes.Count > 0) {
                    nodes.Add(null);
                }
                var height = measured.Heights[measured.Index++];
                if (singleHeight is null) {
                    singleHeight = height;
                }
                else if (!mixedHeights && System.Math.Abs(height - singleHeight.Value) >= Epsilon) {
                    mixedHeights = true;
                }
                var line = new EditorHeightMapText(len, height) { Outdated = false };
                nodes.Add(line);
                pos += len + 1;
            }
            if (pos <= end) {
                nodes.Add(null);
                nodes.Add(new EditorHeightMapGap(end - pos).UpdateHeight(oracle, pos));
            }
            var result = Of(nodes);
            if (mixedHeights
                || singleHeight is null
                || System.Math.Abs(result.Height - Height) >= Epsilon
                || System.Math.Abs(singleHeight.Value - HeightMetrics(oracle, offset).PerLine) >= Epsilon) {
                EditorHeightMapChangeTracker.MarkChanged();
            }
            return ReplaceInstance(this, result);
        }
        if (force || Outdated) {
            SetHeight(oracle.HeightForGap(offset, offset + Length));
            Outdated = false;
        }
        return this;
    }
}
