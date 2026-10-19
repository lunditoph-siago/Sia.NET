using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed record RenameTarget(string Name, string? Error = null);
internal sealed record RenameConflict(string Message, SourcePosition? Location = null);
internal sealed record RenameDocumentEdit(string DocumentId, string Path, string Before, ChangeSpec[] Changes);
internal sealed record RenamePlan(string Symbol, string Name, IReadOnlyList<RenameDocumentEdit> Documents,
    IReadOnlyList<RenameConflict> Conflicts, IReadOnlyList<CSharpSourceDocument> Context, string Origin)
{
    internal bool CanApply => Conflicts.Count == 0 && Documents.Count > 0;
}
