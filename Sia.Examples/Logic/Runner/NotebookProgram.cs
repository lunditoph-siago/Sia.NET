namespace Sia_Examples.Notebook;

public sealed class NotebookProgram
{
    internal NotebookProgram(
        string source,
        bool needsWrapperUsing,
        IReadOnlyList<CellRange> cellRanges,
        IReadOnlyList<CSharpSourceDocument> compilationSources,
        IReadOnlyList<CSharpSourceMapping>? sourceMappings = null)
    {
        Source = source;
        NeedsWrapperUsing = needsWrapperUsing;
        CellRanges = cellRanges;
        CompilationSources = compilationSources;
        SourceMappings = sourceMappings ?? [];
    }

    public string Source { get; }

    public bool NeedsWrapperUsing { get; }

    public IReadOnlyList<CellRange> CellRanges { get; }

    internal IReadOnlyList<CSharpSourceDocument> CompilationSources { get; }
    internal IReadOnlyList<CSharpSourceMapping> SourceMappings { get; }
}
