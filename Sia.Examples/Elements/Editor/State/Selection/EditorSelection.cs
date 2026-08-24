namespace Sia_Examples.Editor;

public sealed class EditorSelection : IEquatable<EditorSelection>
{
    private EditorSelection(SelectionRange[] ranges, int mainIndex)
    {
        Ranges = ranges;
        MainIndex = mainIndex;
    }

    public IReadOnlyList<SelectionRange> Ranges { get; }

    public int MainIndex { get; }

    public SelectionRange Main => Ranges[MainIndex];

    public EditorSelection Map(ChangeDesc change, int assoc = -1)
        => change.IsEmpty
            ? this
            : Create([.. Ranges.Select(range => range.Map(change, assoc))], MainIndex);

    public EditorSelection AsSingle()
        => Ranges.Count == 1 ? this : new([Main], 0);

    public bool Eq(EditorSelection other, bool includeAssoc = false)
    {
        if (Ranges.Count != other.Ranges.Count || MainIndex != other.MainIndex) {
            return false;
        }
        for (var index = 0; index < Ranges.Count; index++) {
            if (!Ranges[index].Eq(other.Ranges[index], includeAssoc)) {
                return false;
            }
        }
        return true;
    }

    public bool Equals(EditorSelection? other)
        => other is not null && Eq(other, includeAssoc: true);

    public override bool Equals(object? obj)
        => obj is EditorSelection selection && Equals(selection);

    public override int GetHashCode() => HashCode.Combine(Ranges.Count, MainIndex);

    public static EditorSelection Single(int anchor, int? head = null)
        => new([Range(anchor, head ?? anchor)], 0);

    public static SelectionRange Cursor(
        int pos,
        int assoc = 0,
        int? bidiLevel = null,
        int? goalColumn = null)
    {
        var flags = assoc switch {
            < 0 => SelFlag.AssocBefore,
            > 0 => SelFlag.AssocAfter,
            _ => (SelFlag)0,
        };
        flags |= bidiLevel is null
            ? SelFlag.BidiMask
            : (SelFlag)Math.Min(6, bidiLevel.Value);
        return new(pos, pos, flags, goalColumn);
    }

    public static SelectionRange Range(
        int anchor,
        int head,
        int? goalColumn = null,
        int? bidiLevel = null,
        int? assoc = null)
    {
        var flags = bidiLevel is null
            ? SelFlag.BidiMask
            : (SelFlag)Math.Min(6, bidiLevel.Value);
        var association = assoc ?? (anchor != head ? head < anchor ? 1 : -1 : 0);
        if (association != 0) {
            flags |= association < 0 ? SelFlag.AssocBefore : SelFlag.AssocAfter;
        }
        return head < anchor
            ? new(head, anchor, flags | SelFlag.Inverted, goalColumn)
            : new(anchor, head, flags, goalColumn);
    }

    public static EditorSelection Create(SelectionRange[] ranges, int mainIndex = 0)
    {
        if (ranges.Length == 0) {
            throw new ArgumentException("Need at least one range", nameof(ranges));
        }

        var position = 0;
        foreach (var range in ranges) {
            if (range.Empty ? range.From <= position : range.From < position) {
                return Normalize(ranges, mainIndex);
            }
            position = range.To;
        }
        return new(ranges, mainIndex);
    }

    internal static EditorSelection Normalize(SelectionRange[] ranges, int mainIndex = 0)
    {
        var main = ranges[mainIndex];
        Array.Sort(ranges, static (left, right) => left.From.CompareTo(right.From));
        mainIndex = Array.IndexOf(ranges, main);
        for (var index = 1; index < ranges.Length; index++) {
            var current = ranges[index];
            var previous = ranges[index - 1];
            if (current.Empty ? current.From > previous.To : current.From >= previous.To) {
                continue;
            }

            var from = previous.From;
            var to = Math.Max(current.To, previous.To);
            if (index <= mainIndex) {
                mainIndex--;
            }
            ranges[index - 1] = current.Anchor > current.Head
                ? Range(to, from)
                : Range(from, to);

            var normalized = new SelectionRange[ranges.Length - 1];
            Array.Copy(ranges, 0, normalized, 0, index);
            if (index < ranges.Length - 1) {
                Array.Copy(
                    ranges,
                    index + 1,
                    normalized,
                    index,
                    ranges.Length - index - 1);
            }
            ranges = normalized;
            index--;
        }
        return new(ranges, mainIndex);
    }
}
