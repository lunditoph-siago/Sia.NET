using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

internal sealed record CSharpCompilationRequest(
    string AssemblyName,
    IReadOnlyList<CSharpSourceDocument> Sources,
    OutputKind OutputKind = OutputKind.ConsoleApplication);
