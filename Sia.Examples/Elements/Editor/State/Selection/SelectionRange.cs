namespace Sia_Examples.Editor;

public sealed class SelectionRange : IEquatable<SelectionRange>
{
    private readonly SelFlag _flags;

    internal SelectionRange(int from, int to, SelFlag flags, int? goalColumn)
    {
        From = from;
        To = to;
        _flags = flags;
        GoalColumn = goalColumn;
    }

    public int From { get; }

    public int To { get; }

    public int? GoalColumn { get; }

    public int Anchor => (_flags & SelFlag.Inverted) != 0 ? To : From;

    public int Head => (_flags & SelFlag.Inverted) != 0 ? From : To;

    public bool Empty => From == To;

    public int Assoc => (_flags & SelFlag.AssocBefore) != 0
        ? -1
        : (_flags & SelFlag.AssocAfter) != 0
            ? 1
            : 0;

    public SelectionRange Map(ChangeDesc change, int assoc = -1)
    {
        if (Empty) {
            var mapped = change.MapPos(From, assoc);
            var position = mapped ?? From;
            return position == From
                ? this
                : new(position, position, _flags, GoalColumn);
        }

        var from = change.MapPos(From, 1) ?? From;
        var to = change.MapPos(To, -1) ?? To;
        return from == From && to == To
            ? this
            : new(from, to, _flags, GoalColumn);
    }

    public bool Eq(SelectionRange other, bool includeAssoc = false)
        => Anchor == other.Anchor
            && Head == other.Head
            && GoalColumn == other.GoalColumn
            && (!includeAssoc || Empty || Assoc == other.Assoc);

    public bool Equals(SelectionRange? other)
        => other is not null && Eq(other, includeAssoc: true);

    public override bool Equals(object? obj)
        => obj is SelectionRange range && Equals(range);

    public override int GetHashCode() => HashCode.Combine(From, To, GoalColumn);

    public override string ToString() => $"Range({Anchor},{Head})";
}
