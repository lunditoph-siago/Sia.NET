namespace Sia_Examples.Notebook;

public readonly record struct NotebookDiagnostic(
    string Id,
    string Message,
    NotebookDiagnosticSeverity Severity,
    int Line,
    int Column,
    bool InUserCode);
