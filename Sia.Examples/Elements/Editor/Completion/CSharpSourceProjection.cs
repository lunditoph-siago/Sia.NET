using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed class CSharpSourceProjection(string activeId, IReadOnlyList<CSharpSourceDocument> originals,
    IReadOnlyList<CSharpSourceMapping> mappings)
{
    private CompilationUnitSyntax? _activeRoot;
    private readonly Dictionary<string, SourceText> _sourceTexts = originals.DistinctBy(static source => source.Id)
        .ToDictionary(static source => source.Id, static source => SourceText.From(source.Source), StringComparer.Ordinal);
    internal string ActiveId { get; } = activeId;
    internal IReadOnlyList<CSharpSourceDocument> Sources => originals;
    internal CSharpSourceDocument ActiveSource => originals.First(source => source.Id == ActiveId);
    private IReadOnlyList<CSharpSourceMapping> Mappings => mappings;

    internal bool HasSameContext(CSharpSourceProjection other)
        => ActiveId == other.ActiveId && originals.SequenceEqual(other.Sources) && mappings.SequenceEqual(other.Mappings);

    internal static async Task<Document> PrepareEditingDocumentAsync(Document document, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation);
        var directives = root!.DescendantTrivia().Where(static trivia => trivia.GetStructure() is LineDirectiveTriviaSyntax directive
            && directive.Line.IsKind(SyntaxKind.HiddenKeyword)).ToArray();
        if (directives.Length == 0) return document;
        var text = await document.GetTextAsync(cancellation);
        return document.WithText(text.WithChanges(directives.Select(static trivia => new TextChange(trivia.FullSpan,
            new string(trivia.ToFullString().Select(static character => character is '\r' or '\n' ? character : ' ').ToArray())))));
    }

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
        CSharpSourceMapping? mapping = null;
        foreach (var candidate in mappings) {
            if (candidate.ProjectPath != path || candidate.ProjectStart > start
                || start + length > candidate.ProjectStart + candidate.Length) continue;
            mapping ??= candidate;
            if (candidate.SourceId != ActiveId) continue;
            mapping = candidate;
            break;
        }
        sourceId = mapping?.SourceId ?? "";
        sourceStart = mapping is null ? 0 : mapping.SourceStart + start - mapping.ProjectStart;
        return mapping is not null;
    }

    internal SourcePosition ToSource(SourcePosition position)
    {
        if (position.Generated) return position;
        if (!TryToSource(position.Path, position.Start, position.Length, out var id, out var start)) return position with { Generated = true };
        var source = originals.First(source => source.Id == id);
        var point = _sourceTexts[id].Lines.GetLinePosition(start);
        return new(id, source.DisplayPath, start, position.Length, point.Line + 1, point.Character + 1, source.Source, false);
    }

    internal CompletionEdit? ToSource(string path, IReadOnlyList<TextChange> edits, int position, CancellationToken cancellation = default,
        CompilationUnitSyntax? projectRoot = null)
    {
        var changes = new List<(TextChange Project, ChangeSpec Source)>();
        var pending = edits.OrderBy(static edit => edit.Span.Start).ToArray();
        if (pending.Any(edit => !TryToSource(path, edit.Span.Start, edit.Span.Length, out var id, out _) || id != ActiveId)) {
            if (projectRoot is null || !ActiveSource.IsUserCode
                || !mappings.Any(mapping => mapping.ProjectPath == path && mapping.SourceId == ActiveId)) return null;
            var headerEnd = Math.Max(projectRoot.Usings.LastOrDefault()?.FullSpan.End ?? 0, projectRoot.Externs.LastOrDefault()?.FullSpan.End ?? 0);
            var headerEdits = pending.Where(edit => edit.Span.End <= headerEnd).ToArray();
            if (headerEdits.Length == 0) return null;
            var text = projectRoot.SyntaxTree.GetText(cancellation);
            var newHeaderEnd = headerEnd + headerEdits.Sum(static edit => (edit.NewText?.Length ?? 0) - edit.Span.Length);
            var header = text.WithChanges(headerEdits).ToString(TextSpan.FromBounds(0, newHeaderEnd));
            var root = CSharpSyntaxTree.ParseText(header, CSharpLanguageOptions.Parse, cancellationToken: cancellation).GetCompilationUnitRoot(cancellation);
            if (root.ContainsDiagnostics || root.Members.Count != 0
                || projectRoot.Usings.Any(old => !root.Usings.Any(next => old.IsEquivalentTo(next)))
                || projectRoot.Externs.Count != root.Externs.Count
                || projectRoot.Externs.Where((old, index) => !old.IsEquivalentTo(root.Externs[index])).Any()) return null;
            var before = CSharpSyntaxTree.ParseText(text.ToString(TextSpan.FromBounds(0, headerEnd)), CSharpLanguageOptions.Parse,
                cancellationToken: cancellation).GetCompilationUnitRoot(cancellation);
            static IEnumerable<string> Trivia(CompilationUnitSyntax unit) => unit.DescendantTrivia()
                .Where(static trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)).Select(static trivia => trivia.ToFullString());
            if (!Trivia(before).SequenceEqual(Trivia(root))) return null;
            var imports = root.Usings.Where(next => !projectRoot.Usings.Any(old => old.IsEquivalentTo(next))).ToArray();
            if (imports.Length == 0) return null;
            changes.Add((new(TextSpan.FromBounds(0, headerEnd), header), ImportIntoSource(imports, cancellation)));
            pending = pending.Where(edit => edit.Span.End > headerEnd).ToArray();
        }
        foreach (var edit in pending) {
            if (TryToSource(path, edit.Span.Start, edit.Span.Length, out var id, out var start) && id == ActiveId)
                changes.Add((edit, new(start, start + edit.Span.Length, edit.NewText ?? "")));
            else return null;
        }
        var delta = 0;
        int? caret = null;
        for (var index = 0; index < changes.Count; index++) {
            var (project, source) = changes[index];
            var newStart = project.Span.Start + delta;
            var length = project.NewText?.Length ?? 0;
            if (newStart <= position && position <= newStart + length) {
                var preceding = changes.Select((change, order) => (change, order))
                    .Where(item => item.change.Source.From < source.From || item.change.Source.From == source.From && item.order < index)
                    .Sum(static item => (item.change.Source.Insert?.Length ?? 0) - ((item.change.Source.To ?? item.change.Source.From) - item.change.Source.From));
                caret = source.From + preceding + position - newStart;
                break;
            }
            if (position < newStart) break;
            delta += length - project.Span.Length;
        }
        if (caret is null) {
            if (!TryToSource(path, position - delta, 0, out var id, out var start) || id != ActiveId) return null;
            caret = start + changes.Where(change => change.Source.From < start)
                .Sum(static change => (change.Source.Insert?.Length ?? 0) - ((change.Source.To ?? change.Source.From) - change.Source.From));
        }
        return new([.. changes.Select(static change => change.Source).OrderBy(static change => change.From)], caret.Value);
    }

    private ChangeSpec ImportIntoSource(IReadOnlyList<UsingDirectiveSyntax> imports, CancellationToken cancellation)
    {
        var source = ActiveSource.Source;
        _activeRoot ??= CSharpSyntaxTree.ParseText(source, CSharpLanguageOptions.Parse, cancellationToken: cancellation).GetCompilationUnitRoot(cancellation);
        var start = _activeRoot.Usings.Count > 0 ? _activeRoot.Usings[0].SpanStart
            : _activeRoot.Externs.Count > 0 ? _activeRoot.Externs[^1].FullSpan.End : _activeRoot.GetFirstToken(includeZeroWidth: true).SpanStart;
        var newline = Text.OfString(source).PreferredNewline;
        var insert = string.Join(newline, imports.Select(static directive => directive.WithoutTrivia().ToFullString()))
            + newline + (_activeRoot.Usings.Count == 0 ? newline : "");
        if (start > 0 && source[start - 1] is not ('\r' or '\n')) insert = newline + insert;
        return new(start, start, insert);
    }
}
