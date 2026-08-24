namespace Sia_Examples.Editor;

public abstract class EditorHeightMap(int length, double height, bool outdated = true)
{
    protected const double Epsilon = 1e-3;

    public int Length { get; protected set; } = length;

    public double Height { get; protected set; } = height;

    public bool Outdated { get; set; } = outdated;

    public virtual int Size => 1;

    public abstract EditorBlockInfo BlockAt(double height, EditorHeightOracle oracle, double top, int offset);

    public abstract EditorBlockInfo LineAt(
        double value, QueryType type, EditorHeightOracle oracle, double top, int offset);

    public abstract void ForEachLine(
        int from, int to, EditorHeightOracle oracle, double top, int offset,
        Action<EditorBlockInfo> action);

    public abstract EditorHeightMap UpdateHeight(
        EditorHeightOracle oracle, int offset = 0, bool force = false,
        EditorMeasuredHeights? measured = null);

    protected void SetHeight(double value)
    {
        if (Math.Abs(Height - value) > Epsilon) {
            EditorHeightMapChangeTracker.MarkChanged();
        }
        Height = value;
    }

    public virtual EditorHeightMap Replace(int from, int to, IReadOnlyList<EditorHeightMap?> nodes)
        => Of(nodes);

    public virtual void DecomposeLeft(int to, List<EditorHeightMap?> result) => result.Add(this);

    public virtual void DecomposeRight(int from, List<EditorHeightMap?> result) => result.Add(this);

    public EditorHeightMap ApplyChanges(
        Text oldDocument,
        EditorHeightOracle oracle,
        IReadOnlyList<(int FromA, int ToA, int FromB, int ToB)> changes)
    {
        EditorHeightMap current = this;
        var newDocument = oracle.Doc;
        for (var index = changes.Count - 1; index >= 0; index--) {
            var (fromA, toA, fromB, toB) = changes[index];
            var start = current.LineAt(fromA, QueryType.ByPosNoHeight, oracle.SetDoc(oldDocument), 0, 0);
            var end = start.To >= toA ? start : current.LineAt(toA, QueryType.ByPosNoHeight, oracle, 0, 0);
            toB += end.To - toA;
            toA = end.To;
            while (index > 0 && start.From <= changes[index - 1].ToA) {
                fromA = changes[index - 1].FromA;
                fromB = changes[index - 1].FromB;
                index--;
                if (fromA < start.From) {
                    start = current.LineAt(fromA, QueryType.ByPosNoHeight, oracle, 0, 0);
                }
            }
            fromB += start.From - fromA;
            fromA = start.From;
            oracle.SetDoc(newDocument);

            var singleLine = start.From == end.From
                && newDocument.LineAt(fromB).Number == newDocument.LineAt(toB).Number;
            var nodes = new List<EditorHeightMap?> { new EditorHeightMapGap(toB - fromB, singleLine) };
            current = ReplaceInstance(current, current.Replace(fromA, toA, nodes));
        }
        return current.UpdateHeight(oracle, 0);
    }

    public static EditorHeightMap Empty() => new EditorHeightMapText(0, 0);

    public static EditorHeightMap Of(IReadOnlyList<EditorHeightMap?> nodesInput)
    {
        var nodes = new List<EditorHeightMap?>(nodesInput);
        if (nodes.Count == 1) {
            return nodes[0]!;
        }

        int i = 0, j = nodes.Count, before = 0, after = 0;
        while (true) {
            if (i == j) {
                if (before > after * 2) {
                    var split = (EditorHeightMapBranch)nodes[i - 1]!;
                    i--;
                    nodes.RemoveAt(i);
                    nodes.InsertRange(i, split.Break ? [split.Left, null, split.Right] : [split.Left, split.Right]);
                    j += 1 + (split.Break ? 1 : 0);
                    before -= split.Size;
                } else if (after > before * 2) {
                    var split = (EditorHeightMapBranch)nodes[j]!;
                    nodes.RemoveAt(j);
                    nodes.InsertRange(j, split.Break ? [split.Left, null, split.Right] : [split.Left, split.Right]);
                    j += 2 + (split.Break ? 1 : 0);
                    after -= split.Size;
                } else {
                    break;
                }
            } else if (before < after) {
                var next = nodes[i++];
                if (next is not null) {
                    before += next.Size;
                }
            } else {
                var next = nodes[--j];
                if (next is not null) {
                    after += next.Size;
                }
            }
        }

        var brk = 0;
        if (IsBreakAt(nodes, i - 1)) {
            brk = 1;
            i--;
        } else if (IsBreakAt(nodes, i)) {
            brk = 1;
            j++;
        }
        return new EditorHeightMapBranch(
            Of(nodes.GetRange(0, i)),
            brk,
            Of(nodes.GetRange(j, nodes.Count - j)));
    }

    private static bool IsBreakAt(List<EditorHeightMap?> nodes, int index)
        => index < 0 || index >= nodes.Count || nodes[index] is null;

    internal static EditorHeightMap ReplaceInstance(EditorHeightMap old, EditorHeightMap value)
    {
        if (ReferenceEquals(old, value)) {
            return old;
        }
        if (old.GetType() != value.GetType()) {
            EditorHeightMapChangeTracker.MarkChanged();
        }
        return value;
    }
}
