namespace Sia_Examples.Notebook;

public sealed record NotebookProgram(
    string Source,
    bool NeedsWrapperUsing,
    IReadOnlyList<CellRange> CellRanges)
{
    public string? ResolveCellId(int line)
    {
        string? owner = null;
        var bestStart = -1;
        foreach (var range in CellRanges) {
            if (range.StatementsStartLine <= line
                && range.StatementsStartLine > bestStart) {
                owner = range.CellId;
                bestStart = range.StatementsStartLine;
            }
            if (range.TypesStartLine is { } typesStart
                && typesStart <= line
                && typesStart > bestStart) {
                owner = range.CellId;
                bestStart = typesStart;
            }
        }
        return owner;
    }
}
