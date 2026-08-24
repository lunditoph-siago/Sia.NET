namespace Sia_Examples.Editor;

public sealed class EditorHeightMapBranch : EditorHeightMap
{
    private int _size;

    public EditorHeightMapBranch(EditorHeightMap left, int brk, EditorHeightMap right)
        : base(left.Length + brk + right.Length, left.Height + right.Height, left.Outdated || right.Outdated)
    {
        Left = left;
        Right = right;
        Break = brk != 0;
        _size = left.Size + right.Size;
    }

    public EditorHeightMap Left { get; private set; }

    public EditorHeightMap Right { get; private set; }

    public bool Break { get; }

    public override int Size => _size;

    public override EditorBlockInfo BlockAt(double height, EditorHeightOracle oracle, double top, int offset)
    {
        var mid = top + Left.Height;
        return height < mid
            ? Left.BlockAt(height, oracle, top, offset)
            : Right.BlockAt(height, oracle, mid, offset + Left.Length + (Break ? 1 : 0));
    }

    public override EditorBlockInfo LineAt(
        double value, QueryType type, EditorHeightOracle oracle, double top, int offset)
    {
        var rightTop = top + Left.Height;
        var rightOffset = offset + Left.Length + (Break ? 1 : 0);
        var left = type == QueryType.ByHeight ? value < rightTop : value < rightOffset;
        var block = left
            ? Left.LineAt(value, type, oracle, top, offset)
            : Right.LineAt(value, type, oracle, rightTop, rightOffset);
        if (Break || (left ? block.To < rightOffset : block.From > rightOffset)) {
            return block;
        }
        var subQuery = type == QueryType.ByPosNoHeight ? QueryType.ByPosNoHeight : QueryType.ByPos;
        return left
            ? block.Join(Right.LineAt(rightOffset, subQuery, oracle, rightTop, rightOffset))
            : Left.LineAt(rightOffset, subQuery, oracle, top, offset).Join(block);
    }

    public override void ForEachLine(
        int from, int to, EditorHeightOracle oracle, double top, int offset, Action<EditorBlockInfo> action)
    {
        var rightTop = top + Left.Height;
        var rightOffset = offset + Left.Length + (Break ? 1 : 0);
        if (Break) {
            if (from < rightOffset) {
                Left.ForEachLine(from, to, oracle, top, offset, action);
            }
            if (to >= rightOffset) {
                Right.ForEachLine(from, to, oracle, rightTop, rightOffset, action);
            }
        } else {
            var mid = LineAt(rightOffset, QueryType.ByPos, oracle, top, offset);
            if (from < mid.From) {
                Left.ForEachLine(from, mid.From - 1, oracle, top, offset, action);
            }
            if (mid.To >= from && mid.From <= to) {
                action(mid);
            }
            if (to > mid.To) {
                Right.ForEachLine(mid.To + 1, to, oracle, rightTop, rightOffset, action);
            }
        }
    }

    public override EditorHeightMap Replace(int from, int to, IReadOnlyList<EditorHeightMap?> nodes)
    {
        var rightStart = Left.Length + (Break ? 1 : 0);
        if (to < rightStart) {
            return Balanced(Left.Replace(from, to, nodes), Right);
        }
        if (from > Left.Length) {
            return Balanced(Left, Right.Replace(from - rightStart, to - rightStart, nodes));
        }

        var result = new List<EditorHeightMap?>();
        if (from > 0) {
            DecomposeLeft(from, result);
        }
        var left = result.Count;
        result.AddRange(nodes);
        if (from > 0) {
            MergeGaps(result, left - 1);
        }
        if (to < Length) {
            var right = result.Count;
            DecomposeRight(to, result);
            MergeGaps(result, right);
        }
        return Of(result);
    }

    public override void DecomposeLeft(int to, List<EditorHeightMap?> result)
    {
        var left = Left.Length;
        if (to <= left) {
            Left.DecomposeLeft(to, result);
            return;
        }
        result.Add(Left);
        if (Break) {
            left++;
            if (to >= left) {
                result.Add(null);
            }
        }
        if (to > left) {
            Right.DecomposeLeft(to - left, result);
        }
    }

    public override void DecomposeRight(int from, List<EditorHeightMap?> result)
    {
        var left = Left.Length;
        var right = left + (Break ? 1 : 0);
        if (from >= right) {
            Right.DecomposeRight(from - right, result);
            return;
        }
        if (from < left) {
            Left.DecomposeRight(from, result);
        }
        if (Break && from < right) {
            result.Add(null);
        }
        result.Add(Right);
    }

    private EditorHeightMap Balanced(EditorHeightMap left, EditorHeightMap right)
    {
        if (left.Size > 2 * right.Size || right.Size > 2 * left.Size) {
            return Of(Break
                ? [left, null, right]
                : [left, right]);
        }
        Left = ReplaceInstance(Left, left);
        Right = ReplaceInstance(Right, right);
        SetHeight(left.Height + right.Height);
        Outdated = left.Outdated || right.Outdated;
        _size = left.Size + right.Size;
        Length = left.Length + (Break ? 1 : 0) + right.Length;
        return this;
    }

    public override EditorHeightMap UpdateHeight(
        EditorHeightOracle oracle, int offset = 0, bool force = false, EditorMeasuredHeights? measured = null)
    {
        var rightStart = offset + Left.Length + (Break ? 1 : 0);
        var left = Left;
        var right = Right;
        var rebalance = false;

        if (measured is not null && measured.From <= offset + Left.Length && measured.More) {
            left = Left.UpdateHeight(oracle, offset, force, measured);
            rebalance = true;
        } else {
            Left.UpdateHeight(oracle, offset, force);
        }
        if (measured is not null && measured.From <= rightStart + Right.Length && measured.More) {
            right = Right.UpdateHeight(oracle, rightStart, force, measured);
            rebalance = true;
        } else {
            Right.UpdateHeight(oracle, rightStart, force);
        }
        if (rebalance) {
            return Balanced(left, right);
        }
        SetHeight(Left.Height + Right.Height);
        Outdated = false;
        return this;
    }

    private static void MergeGaps(List<EditorHeightMap?> nodes, int around)
    {
        if (around >= 0 && around < nodes.Count && nodes[around] is null
            && around - 1 >= 0 && nodes[around - 1] is EditorHeightMapGap before
            && around + 1 < nodes.Count && nodes[around + 1] is EditorHeightMapGap after) {
            nodes.RemoveRange(around - 1, 3);
            nodes.Insert(around - 1, new EditorHeightMapGap(before.Length + 1 + after.Length));
        }
    }
}
