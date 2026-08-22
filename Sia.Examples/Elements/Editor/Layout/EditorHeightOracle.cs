namespace Sia_Examples.Editor;

public sealed class EditorHeightOracle(bool lineWrapping)
{
    private readonly HashSet<int> _heightSamples = [];

    public Text Doc { get; private set; } = Text.Empty;

    public bool LineWrapping { get; private set; } = lineWrapping;

    public double LineHeight { get; private set; } = 14;

    public double CharWidth { get; private set; } = 7;

    public double TextHeight { get; private set; } = 14;

    public double LineLength { get; private set; } = 30;

    public EditorHeightOracle SetDoc(Text doc)
    {
        Doc = doc;
        return this;
    }

    public double HeightForGap(int from, int to)
    {
        var lines = Doc.LineAt(to).Number - Doc.LineAt(from).Number + 1;
        if (LineWrapping) {
            lines += Math.Max(
                0,
                (int)Math.Ceiling(((to - from) - lines * LineLength * 0.5) / LineLength));
        }
        return LineHeight * lines;
    }

    public double HeightForLine(int length)
    {
        if (!LineWrapping) {
            return LineHeight;
        }
        var lines = 1 + Math.Max(
            0,
            (int)Math.Ceiling((length - LineLength) / Math.Max(1, LineLength - 5)));
        return lines * LineHeight;
    }

    public bool MustRefreshForHeights(IReadOnlyList<double> lineHeights)
    {
        var changed = false;
        foreach (var height in lineHeights) {
            if (_heightSamples.Add((int)Math.Floor(height * 10))) {
                changed = true;
            }
        }
        return changed;
    }

    public bool Refresh(
        bool lineWrapping,
        double lineHeight,
        double charWidth,
        double textHeight,
        double lineLength,
        IReadOnlyList<double> knownHeights)
    {
        var changed = Math.Abs(lineHeight - LineHeight) > 0.3 || LineWrapping != lineWrapping;
        LineWrapping = lineWrapping;
        LineHeight = lineHeight;
        CharWidth = charWidth;
        TextHeight = textHeight;
        LineLength = lineLength;
        if (changed) {
            _heightSamples.Clear();
            foreach (var height in knownHeights) {
                _heightSamples.Add((int)Math.Floor(height * 10));
            }
        }
        return changed;
    }
}
