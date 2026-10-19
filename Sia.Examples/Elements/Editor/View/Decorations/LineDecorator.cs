namespace Sia.Examples.Editor;

public static class LineDecorator
{
    public static List<StyledRun> Segment(
        string lineText,
        int lineFrom,
        IEnumerable<Range<Decoration>> marks)
    {
        var active = new List<Range<Decoration>>();
        SortedSet<int>? boundaries = null;
        var previousTo = 0;
        foreach (var mark in marks) {
            if (mark.Value.Kind != DecorationKind.Mark) {
                continue;
            }
            var from = System.Math.Max(0, mark.From - lineFrom);
            var to = System.Math.Min(lineText.Length, mark.To - lineFrom);
            if (to <= from) {
                continue;
            }
            if (boundaries is null && from < previousTo) {
                boundaries = new() { 0, lineText.Length };
                foreach (var range in active) {
                    boundaries.Add(range.From);
                    boundaries.Add(range.To);
                }
            }
            boundaries?.Add(from);
            boundaries?.Add(to);
            previousTo = to;
            active.Add(new(from, to, mark.Value));
        }
        if (active.Count == 0) {
            return [new(lineText, null)];
        }

        if (boundaries is null) {
            var orderedRuns = new List<StyledRun>(active.Count * 2 + 1);
            var position = 0;
            foreach (var range in active) {
                if (position < range.From) {
                    orderedRuns.Add(new(lineText[position..range.From], null));
                }
                orderedRuns.Add(new(lineText[range.From..range.To], range.Value.Class));
                position = range.To;
            }
            if (position < lineText.Length) {
                orderedRuns.Add(new(lineText[position..], null));
            }
            return orderedRuns;
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
