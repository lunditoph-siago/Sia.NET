namespace Sia_Examples.Notebook;

public sealed class NotebookProgram
{
    internal NotebookProgram(
        string source,
        bool needsWrapperUsing,
        IReadOnlyList<CellRange> cellRanges,
        IReadOnlyList<CSharpSourceDocument> compilationSources)
    {
        Source = source;
        NeedsWrapperUsing = needsWrapperUsing;
        CellRanges = cellRanges;
        CompilationSources = compilationSources;
    }

    public string Source { get; }

    public bool NeedsWrapperUsing { get; }

    public IReadOnlyList<CellRange> CellRanges { get; }

    internal IReadOnlyList<CSharpSourceDocument> CompilationSources { get; }
}
