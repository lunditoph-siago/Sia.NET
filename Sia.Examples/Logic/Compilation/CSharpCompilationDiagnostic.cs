namespace Sia_Examples.Notebook;

internal sealed record CSharpCompilationDiagnostic(
    string Id,
    string Message,
    NotebookDiagnosticSeverity Severity,
    string? SourceId,
    string? SourcePath,
    int Line,
    int Column);
