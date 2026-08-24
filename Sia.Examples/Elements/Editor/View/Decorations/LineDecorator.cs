namespace Sia_Examples.Editor;

public static class LineDecorator
{
    public static List<StyledRun> Segment(
        string lineText,
        int lineFrom,
        IEnumerable<Range<Decoration>> marks)
    {
        var active = new List<Range<Decoration>>();
        var boundaries = new SortedSet<int> { 0, lineText.Length };
        foreach (var mark in marks) {
            if (mark.Value.Kind != DecorationKind.Mark) {
                continue;
            }
            var from = Math.Max(0, mark.From - lineFrom);
            var to = Math.Min(lineText.Length, mark.To - lineFrom);
            if (to <= from) {
                continue;
            }
            boundaries.Add(from);
            boundaries.Add(to);
            active.Add(new(from, to, mark.Value));
        }
        if (active.Count == 0) {
            return [new(lineText, null)];
        }

        var points = boundaries.ToArray();
        var runs = new List<StyledRun>(points.Length);
        for (var index = 0; index < points.Length - 1; index++) {
            var from = points[index];
            var to = points[index + 1];
            if (from >= to) {
                continue;
            }

            string? className = null;
            foreach (var range in active) {
                if (range.From > from || range.To < to) {
                    continue;
                }
                className = className is null
                    ? range.Value.Class
                    : $"{className} {range.Value.Class}";
            }
            runs.Add(new(lineText[from..to], className));
        }
        return runs;
    }
}
