namespace Sia_Examples.Editor;

public readonly record struct EditorViewport(int From, int To)
{
    public bool Empty => From >= To;

    public bool Contains(int position) => position >= From && position <= To;

    public bool Overlaps(EditorViewport other) => From <= other.To && other.From <= To;

    public static EditorViewport All(int length) => new(0, length);

    public EditorViewport Clamp(int length)
    {
        var from = Math.Clamp(From, 0, length);
        var to = Math.Clamp(To, from, length);
        return from == From && to == To ? this : new(from, to);
    }

    public EditorViewport Map(ChangeDesc change)
    {
        var from = change.MapPos(From, -1) ?? From;
        var to = change.MapPos(To, 1) ?? To;
        return from == From && to == To ? this : new(from, to);
    }

    public static IReadOnlyList<EditorViewport> Merge(IReadOnlyList<EditorViewport> viewports)
    {
        if (viewports.Count <= 1) {
            return viewports;
        }
        var sorted = viewports.OrderBy(v => v.From).ToList();
        var merged = new List<EditorViewport> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++) {
            var last = merged[^1];
            var next = sorted[i];
            if (next.From <= last.To) {
                merged[^1] = new EditorViewport(last.From, Math.Max(last.To, next.To));
            } else {
                merged.Add(next);
            }
        }
        return merged;
    }
}
