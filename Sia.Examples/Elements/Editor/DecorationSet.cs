namespace Sia_Examples.Editor;

public static class DecorationSet
{
    public static RangeSet<Decoration> Empty { get; } = RangeSet<Decoration>.Empty;

    public static RangeSet<Decoration> Marks(
        IEnumerable<(int From, int To, string Class)> marks)
        => RangeSet<Decoration>.Of(
            marks
                .Where(static mark => mark.To > mark.From)
                .Select(static mark => new Range<Decoration>(
                    mark.From,
                    mark.To,
                    Decoration.Mark(mark.Class))),
            sort: true);

    public static string? LineClass(
        this RangeSet<Decoration> decorations,
        int lineFrom,
        int lineTo)
    {
        string? className = null;
        foreach (var range in decorations.Between(lineFrom, lineTo + 1)) {
            if (range.Value.Kind != DecorationKind.Line) {
                continue;
            }
            className = className is null
                ? range.Value.Class
                : $"{className} {range.Value.Class}";
        }
        return className;
    }
}
