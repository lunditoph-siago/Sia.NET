namespace Sia_Examples.Editor;

[Flags]
internal enum SelFlag : byte
{
    BidiMask = 7, AssocBefore = 8, AssocAfter = 16, Inverted = 32, Undirectional = 64
}

public sealed class SelectionRange : IEquatable<SelectionRange>
{
    public int From { get; }
    public int To { get; }
    private readonly SelFlag _flags;
    public int? GoalColumn { get; }

    internal SelectionRange(int from, int to, SelFlag flags, int? goalColumn)
    { From = from; To = to; _flags = flags; GoalColumn = goalColumn; }

    public int Anchor => (_flags & SelFlag.Inverted) != 0 ? To : From;
    public int Head => (_flags & SelFlag.Inverted) != 0 ? From : To;
    public bool Empty => From == To;
    public int Assoc => (_flags & SelFlag.AssocBefore) != 0 ? -1 : (_flags & SelFlag.AssocAfter) != 0 ? 1 : 0;

    public SelectionRange Map(ChangeDesc change, int assoc = -1)
    {
        if (Empty) { var m = change.MapPos(From, assoc); var p = m ?? From; return p == From ? this : new SelectionRange(p, p, _flags, GoalColumn); }
        int f = change.MapPos(From, 1) ?? From, t = change.MapPos(To, -1) ?? To;
        return f == From && t == To ? this : new SelectionRange(f, t, _flags, GoalColumn);
    }

    public bool Eq(SelectionRange other, bool includeAssoc = false)
        => Anchor == other.Anchor && Head == other.Head && GoalColumn == other.GoalColumn
            && (!includeAssoc || Empty || Assoc == other.Assoc);

    public bool Equals(SelectionRange? other) => other is not null && Eq(other, true);
    public override bool Equals(object? obj) => obj is SelectionRange r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(From, To, GoalColumn);
    public override string ToString() => $"Range({Anchor},{Head})";
}

public sealed class EditorSelection : IEquatable<EditorSelection>
{
    public IReadOnlyList<SelectionRange> Ranges { get; }
    public int MainIndex { get; }

    private EditorSelection(SelectionRange[] ranges, int mainIndex) { Ranges = ranges; MainIndex = mainIndex; }

    public EditorSelection Map(ChangeDesc change, int assoc = -1)
        => change.IsEmpty ? this : Create([.. Ranges.Select(r => r.Map(change, assoc))], MainIndex);

    public SelectionRange Main => Ranges[MainIndex];

    public EditorSelection AsSingle() => Ranges.Count == 1 ? this : new EditorSelection([Main], 0);

    public bool Eq(EditorSelection other, bool includeAssoc = false)
    {
        if (Ranges.Count != other.Ranges.Count || MainIndex != other.MainIndex) return false;
        for (var i = 0; i < Ranges.Count; i++) if (!Ranges[i].Eq(other.Ranges[i], includeAssoc)) return false;
        return true;
    }

    public bool Equals(EditorSelection? other) => other is not null && Eq(other, true);
    public override bool Equals(object? obj) => obj is EditorSelection s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(Ranges.Count, MainIndex);

    public static EditorSelection Single(int anchor, int? head = null)
        => new([Range(anchor, head ?? anchor)], 0);

    public static SelectionRange Cursor(int pos, int assoc = 0, int? bidiLevel = null, int? goalColumn = null)
    {
        var flags = (SelFlag)(assoc == 0 ? 0 : assoc < 0 ? (int)SelFlag.AssocBefore : (int)SelFlag.AssocAfter);
        flags |= bidiLevel == null ? (SelFlag)7 : (SelFlag)Math.Min(6, bidiLevel.Value);
        return new SelectionRange(pos, pos, flags, goalColumn);
    }

    public static SelectionRange Range(int anchor, int head, int? goalColumn = null, int? bidiLevel = null, int? assoc = null)
    {
        var flags = bidiLevel == null ? (SelFlag)7 : (SelFlag)Math.Min(6, bidiLevel.Value);
        var a = assoc ?? (anchor != head ? (head < anchor ? 1 : -1) : 0);
        if (a != 0) flags |= a < 0 ? SelFlag.AssocBefore : SelFlag.AssocAfter;
        return head < anchor
            ? new SelectionRange(head, anchor, flags | SelFlag.Inverted, goalColumn)
            : new SelectionRange(anchor, head, flags, goalColumn);
    }

    public static EditorSelection Create(SelectionRange[] ranges, int mainIndex = 0)
    {
        if (ranges.Length == 0) throw new ArgumentException("Need at least one range");
        for (int pos = 0, i = 0; i < ranges.Length; i++) { var r = ranges[i]; if (r.Empty ? r.From <= pos : r.From < pos) return Normalize(ranges, mainIndex); pos = r.To; }
        return new EditorSelection(ranges, mainIndex);
    }

    internal static EditorSelection Normalize(SelectionRange[] ranges, int mainIndex = 0)
    {
        var main = ranges[mainIndex];
        Array.Sort(ranges, (a, b) => a.From.CompareTo(b.From));
        mainIndex = Array.IndexOf(ranges, main);
        for (var i = 1; i < ranges.Length; i++) {
            var r = ranges[i]; var p = ranges[i - 1];
            if (r.Empty ? r.From <= p.To : r.From < p.To) {
                int f = p.From, t = Math.Max(r.To, p.To);
                if (i <= mainIndex) mainIndex--;
                ranges[i - 1] = r.Anchor > r.Head ? Range(t, f) : Range(f, t);
                var nr = new SelectionRange[ranges.Length - 1];
                Array.Copy(ranges, 0, nr, 0, i);
                if (i < ranges.Length - 1) Array.Copy(ranges, i + 1, nr, i, ranges.Length - i - 1);
                ranges = nr; i--;
            }
        }
        return new EditorSelection(ranges, mainIndex);
    }
}
