namespace Sia.Examples.Notebook;

public sealed record NotebookProgramCell(
    string Id,
    IReadOnlyList<NotebookProgramFile> Files);
