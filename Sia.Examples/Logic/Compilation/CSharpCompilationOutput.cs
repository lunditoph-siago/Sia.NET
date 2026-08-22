namespace Sia_Examples.Notebook;

internal sealed record CSharpCompilationOutput(
    bool Success,
    byte[]? AssemblyImage,
    IReadOnlyList<CSharpCompilationDiagnostic> Diagnostics);
