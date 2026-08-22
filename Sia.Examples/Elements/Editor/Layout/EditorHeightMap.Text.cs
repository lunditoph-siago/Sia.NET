namespace Sia_Examples.Editor;

public sealed class EditorHeightMapText(int length, double height) : EditorHeightMap(length, height)
{
    public override EditorBlockInfo BlockAt(double height, EditorHeightOracle oracle, double top, int offset)
        => new(offset, Length, top, Height);

    public override EditorBlockInfo LineAt(
        double value, QueryType type, EditorHeightOracle oracle, double top, int offset)
        => new(offset, Length, top, Height);

    public override void ForEachLine(
        int from, int to, EditorHeightOracle oracle, double top, int offset, Action<EditorBlockInfo> action)
    {
        if (from <= offset + Length && to >= offset) {
            action(new EditorBlockInfo(offset, Length, top, Height));
        }
    }

    public override EditorHeightMap Replace(int from, int to, IReadOnlyList<EditorHeightMap?> nodes)
    {
        if (nodes.Count == 1) {
            var candidate = nodes[0];
            var singleLineGap = candidate is EditorHeightMapGap { IsSingleLine: true };
            if (candidate is not null && (candidate is EditorHeightMapText || singleLineGap)
                && Math.Abs(Length - candidate.Length) < 10) {
                EditorHeightMapText node;
                if (candidate is EditorHeightMapGap gap) {
                    node = new EditorHeightMapText(gap.Length, Height);
                } else {
                    node = (EditorHeightMapText)candidate;
                    node.SetHeight(Height);
                }
                if (!Outdated) {
                    node.Outdated = false;
                }
                return node;
            }
        }
        return Of(nodes);
    }

    public override EditorHeightMap UpdateHeight(
        EditorHeightOracle oracle, int offset = 0, bool force = false, EditorMeasuredHeights? measured = null)
    {
        if (measured is not null && measured.From <= offset && measured.More) {
            SetHeight(measured.Heights[measured.Index++]);
        } else if (force || Outdated) {
            SetHeight(oracle.HeightForLine(Length));
        }
        Outdated = false;
        return this;
    }
}
