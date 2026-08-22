namespace Sia_Examples.Notebook;

internal sealed record CSharpCompilationRequest(
    string AssemblyName,
    IReadOnlyList<CSharpSourceDocument> Sources);
