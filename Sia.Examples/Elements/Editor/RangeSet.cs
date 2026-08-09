namespace Sia_Examples.Editor;

public sealed class RangeSet<T>
{
    private readonly Range<T>[] _ranges;

    private RangeSet(Range<T>[] ranges) => _ranges = ranges;

    public static RangeSet<T> Empty { get; } = new([]);

    public int Count => _ranges.Length;

    public bool IsEmpty => _ranges.Length == 0;

    public static RangeSet<T> Of(IEnumerable<Range<T>> ranges, bool sort = false)
    {
        var values = ranges as Range<T>[] ?? [.. ranges];
        if (sort) {
            Array.Sort(values, static (left, right) => left.From != right.From
                ? left.From.CompareTo(right.From)
                : left.To.CompareTo(right.To));
        }
        return values.Length == 0 ? Empty : new(values);
    }

    public IEnumerable<Range<T>> Between(int from, int to)
    {
        foreach (var range in _ranges) {
            var overlaps = range.From == range.To
                ? range.From >= from && range.From <= to
                : range.From < to && range.To > from;
            if (overlaps) {
                yield return range;
            }
        }
    }

    public RangeSet<T> Map(ChangeDesc changes)
    {
        if (changes.IsEmpty || _ranges.Length == 0) {
            return this;
        }
        var mapped = new List<Range<T>>(_ranges.Length);
        foreach (var range in _ranges) {
            var point = range.From == range.To;
            var from = changes.MapPos(
                range.From,
                1,
                point ? MapMode.TrackBefore : MapMode.TrackDel);
            var to = point
                ? from
                : changes.MapPos(range.To, -1, MapMode.TrackDel);
            if (from is null || to is null || from > to) {
                continue;
            }
            mapped.Add(new(from.Value, to.Value, range.Value));
        }
        return mapped.Count == 0 ? Empty : new([.. mapped]);
    }
}
