using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed class CSharpSourceProjection(string activeId, IReadOnlyList<CSharpSourceDocument> originals,
    IReadOnlyList<CSharpSourceMapping> mappings)
{
    internal string ActiveId { get; } = activeId;
    internal IReadOnlyList<CSharpSourceDocument> Sources => originals;
    internal CSharpSourceDocument ActiveSource => originals.First(source => source.Id == ActiveId);

    internal (string Path, int Position) ToProject(int position)
    {
        var mapping = mappings.Where(mapping => mapping.SourceId == ActiveId
                && mapping.SourceStart <= position && position <= mapping.SourceStart + mapping.Length)
            .OrderByDescending(static mapping => mapping.SourceStart).ThenByDescending(static mapping => mapping.Length).FirstOrDefault()
            ?? throw new InvalidOperationException("The source position has no compilation mapping.");
        return (mapping.ProjectPath, mapping.ProjectStart + position - mapping.SourceStart);
    }

    internal bool TryToSource(string path, int start, int length, out string sourceId, out int sourceStart)
    {
        var mapping = mappings.Where(mapping => mapping.ProjectPath == path && mapping.ProjectStart <= start
            && start + length <= mapping.ProjectStart + mapping.Length)
            .OrderByDescending(mapping => mapping.SourceId == ActiveId).FirstOrDefault();
        sourceId = mapping?.SourceId ?? "";
        sourceStart = mapping is null ? 0 : mapping.SourceStart + start - mapping.ProjectStart;
        return mapping is not null;
    }

    internal SourcePosition ToSource(SourcePosition position)
    {
        if (position.Generated) return position;
        if (!TryToSource(position.Path, position.Start, position.Length, out var id, out var start)) return position with { Generated = true };
        var source = originals.First(source => source.Id == id);
        var point = SourceText.From(source.Source).Lines.GetLinePosition(start);
        return new(id, source.DisplayPath, start, position.Length, point.Line + 1, point.Character + 1, source.Source, false);
    }

    internal CompletionEdit? ToSource(string path, IReadOnlyList<TextChange> edits, int position)
    {
        var changes = new List<(TextChange Project, ChangeSpec Source)>();
        foreach (var edit in edits.OrderBy(static edit => edit.Span.Start)) {
            if (!TryToSource(path, edit.Span.Start, edit.Span.Length, out var id, out var start) || id != ActiveId) return null;
            changes.Add((edit, new(start, start + edit.Span.Length, edit.NewText ?? "")));
        }
        var delta = 0;
        int? caret = null;
        foreach (var (project, source) in changes) {
            var newStart = project.Span.Start + delta;
            var length = project.NewText?.Length ?? 0;
            if (newStart <= position && position <= newStart + length) {
                var preceding = changes.Where(change => change.Source.From < source.From)
                    .Sum(static change => (change.Source.Insert?.Length ?? 0) - change.Project.Span.Length);
                caret = source.From + preceding + position - newStart;
                break;
            }
            if (position < newStart) break;
            delta += length - project.Span.Length;
        }
        if (caret is null) {
            if (!TryToSource(path, position - delta, 0, out var id, out var start) || id != ActiveId) return null;
            caret = start + changes.Where(change => change.Source.From < start)
                .Sum(static change => (change.Source.Insert?.Length ?? 0) - change.Project.Span.Length);
        }
        return new([.. changes.Select(static change => change.Source).OrderBy(static change => change.From)], caret.Value);
    }
}
