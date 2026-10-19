using Microsoft.CodeAnalysis;

namespace Sia.Examples.Notebook;

internal sealed record CSharpCompilationRequest(
    string AssemblyName,
    IReadOnlyList<CSharpSourceDocument> Sources,
    OutputKind OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
    Func<Compilation, CancellationToken, OutputKind>? SelectOutputKind = null);
