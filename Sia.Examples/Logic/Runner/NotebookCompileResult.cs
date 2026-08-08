namespace Sia_Examples.Notebook;

public sealed record NotebookCompileResult(
    bool Success,
    byte[]? AssemblyImage,
    IReadOnlyList<NotebookDiagnostic> Diagnostics);
