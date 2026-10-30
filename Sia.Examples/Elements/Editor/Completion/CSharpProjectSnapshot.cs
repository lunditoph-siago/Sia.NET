using Microsoft.CodeAnalysis;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed record CSharpProjectSnapshot(string ActiveDocumentId, IReadOnlyList<CSharpSourceDocument> Documents)
{
    internal OutputKind OutputKind { get; init; } = Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary;
    internal Func<Compilation, CancellationToken, OutputKind>? SelectOutputKind { get; init; }
    internal string GlobalUsings { get; init; } = NotebookLanguageContext.GlobalUsings;
    internal CSharpSourceProjection? Projection { get; init; }

    internal static CSharpProjectSnapshot ForNotebook(string activeId, IReadOnlyList<NotebookProgramCell> cells, CancellationToken cancellationToken = default,
        IReadOnlySet<string>? readOnly = null)
    {
        var program = NotebookProgramBuilder.Build(cells, cancellationToken);
        var originals = cells.SelectMany(static cell => cell.Files).Select(file =>
            new CSharpSourceDocument(file.Id, file.Name, file.Name, file.Source, readOnly?.Contains(file.Id) != true)).ToArray();
        return new(activeId, [..program.CompilationSources, new("$render-support", "Notebook/Render.g.cs", "Render.g.cs",
            NotebookProgramBuilder.BuildStandaloneRenderSupport(), false)]) {
            OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            GlobalUsings = NotebookLanguageContext.GetGlobalUsings(program.NeedsWrapperUsing),
            Projection = new(activeId, originals, program.SourceMappings)
        };
    }
}
