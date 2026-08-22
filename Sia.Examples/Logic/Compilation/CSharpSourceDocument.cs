namespace Sia_Examples.Notebook;

internal sealed record CSharpSourceDocument(
    string Id,
    string Path,
    string DisplayPath,
    string Source,
    bool IsUserCode = true);
