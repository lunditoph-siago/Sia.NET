namespace Sia_Examples.Editor;

public enum DecorationKind { Mark, Line }

public sealed record Decoration(DecorationKind Kind, string Class)
{
    public static Decoration Mark(string cssClass) => new(DecorationKind.Mark, cssClass);
    public static Decoration Line(string cssClass) => new(DecorationKind.Line, cssClass);
}

public readonly record struct StyledRun(string Text, string? Class);

public static class DecorationSet
{
    public static readonly RangeSet<Decoration> Empty = RangeSet<Decoration>.Empty;

    public static RangeSet<Decoration> Marks(IEnumerable<(int From, int To, string Class)> marks)
        => RangeSet<Decoration>.Of(marks.Where(m => m.To > m.From)
            .Select(m => new Range<Decoration>(m.From, m.To, Decoration.Mark(m.Class))), sort: true);

    public static string? LineClass(this RangeSet<Decoration> decorations, int lineFrom, int lineTo)
    {
        string? cls = null;
        foreach (var r in decorations.Between(lineFrom, lineTo + 1)) {
            if (r.Value.Kind != DecorationKind.Line) continue;
            cls = cls is null ? r.Value.Class : cls + " " + r.Value.Class;
        }
        return cls;
    }
}

public static class LineDecorator
{
    public static List<StyledRun> Segment(string lineText, int lineFrom, IEnumerable<Range<Decoration>> marks)
    {
        var active = new List<Range<Decoration>>();
        var boundaries = new SortedSet<int> { 0, lineText.Length };
        foreach (var m in marks) {
            if (m.Value.Kind != DecorationKind.Mark) continue;
            var from = Math.Max(0, m.From - lineFrom);
            var to = Math.Min(lineText.Length, m.To - lineFrom);
            if (to <= from) continue;
            boundaries.Add(from);
            boundaries.Add(to);
            active.Add(new Range<Decoration>(from, to, m.Value));
        }
        if (active.Count == 0) return [new StyledRun(lineText, null)];

        var points = boundaries.ToArray();
        var runs = new List<StyledRun>(points.Length);
        for (var i = 0; i < points.Length - 1; i++) {
            int from = points[i], to = points[i + 1];
            if (from >= to) continue;
            string? cls = null;
            foreach (var a in active) {
                if (a.From > from || a.To < to) continue;
                cls = cls is null ? a.Value.Class : cls + " " + a.Value.Class;
            }
            runs.Add(new StyledRun(lineText[from..to], cls));
        }
        return runs;
    }
}
