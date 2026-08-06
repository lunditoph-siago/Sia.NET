namespace Sia_Examples.Editor;

public readonly record struct Range<T>(int From, int To, T Value);

public sealed class RangeSet<T>
{
    private readonly Range<T>[] _ranges;

    private RangeSet(Range<T>[] ranges) => _ranges = ranges;

    public static readonly RangeSet<T> Empty = new([]);

    public int Count => _ranges.Length;
    public bool IsEmpty => _ranges.Length == 0;

    public static RangeSet<T> Of(IEnumerable<Range<T>> ranges, bool sort = false)
    {
        var arr = ranges as Range<T>[] ?? [.. ranges];
        if (sort) Array.Sort(arr, (a, b) => a.From != b.From ? a.From - b.From : a.To - b.To);
        return arr.Length == 0 ? Empty : new RangeSet<T>(arr);
    }

    public IEnumerable<Range<T>> Between(int from, int to)
    {
        foreach (var r in _ranges) {
            if (r.From == r.To ? r.From >= from && r.From <= to : r.From < to && r.To > from) {
                yield return r;
            }
        }
    }

    public RangeSet<T> Map(ChangeDesc changes)
    {
        if (changes.IsEmpty || _ranges.Length == 0) return this;
        var mapped = new List<Range<T>>(_ranges.Length);
        foreach (var r in _ranges) {
            var point = r.From == r.To;
            var from = changes.MapPos(r.From, 1, point ? MapMode.TrackBefore : MapMode.TrackDel);
            var to = point ? from : changes.MapPos(r.To, -1, MapMode.TrackDel);
            if (from is null || to is null || from > to) continue;
            mapped.Add(new Range<T>(from.Value, to.Value, r.Value));
        }
        return mapped.Count == 0 ? Empty : new RangeSet<T>([.. mapped]);
    }
}
