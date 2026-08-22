namespace Sia_Examples.Notebook;

public sealed record NotebookProgramCell(
    string Id,
    IReadOnlyList<NotebookProgramFile> Files);
