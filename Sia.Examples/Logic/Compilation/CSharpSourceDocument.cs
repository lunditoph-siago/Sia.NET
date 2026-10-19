namespace Sia.Examples.Notebook;

internal sealed record CSharpSourceDocument(
    string Id,
    string Path,
    string DisplayPath,
    string Source,
    bool IsUserCode = true);

internal sealed record CSharpSourceMapping(string ProjectPath, int ProjectStart, string SourceId, int SourceStart, int Length);
