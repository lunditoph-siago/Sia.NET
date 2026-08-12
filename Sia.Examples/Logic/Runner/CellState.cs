using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct CellState(
    CellPhase Phase,
    string Source,
    ImmutableArray<HighlightRun> Highlights,
    ImmutableArray<NotebookDiagnostic> Diagnostics,
    string StandardOutput,
    string StandardError,
    bool RenderRequested,
    string RenderOutput)
{
    public static CellState Create(string source)
        => new(
            CellPhase.Idle,
            source,
            [],
            [],
            string.Empty,
            string.Empty,
            false,
            string.Empty);
}
