using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Editor;

internal sealed record SourcePosition(
    string DocumentId, string Path, int Start, int Length, int Line, int Column, string Source, bool Generated);
internal sealed record SymbolDefinition(string Display, IReadOnlyList<SourcePosition> Locations);
internal sealed record LanguageIssue(string Id, string Message, bool Error, SourcePosition? Location);

internal static class CSharpDocumentTools
{
    internal static async Task<SymbolDefinition?> DefinitionAsync(
        Document document, int position, Func<Document, string?> documentKey, CancellationToken cancellation)
    {
        var text = await document.GetTextAsync(cancellation);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, Math.Clamp(position, 0, text.Length), cancellation);
        if (symbol is null) return null;
        var definition = await SymbolFinder.FindSourceDefinitionAsync(symbol, document.Project.Solution, cancellation) ?? symbol;
        var locations = new List<SourcePosition>();
        var sources = new Dictionary<DocumentId, (SourceText Text, string Source)>();
        foreach (var location in definition.Locations.Where(static location => location.IsInSource)) {
            var target = await PositionAsync(document.Project, location, documentKey, cancellation, sources);
            if (target is not null) locations.Add(target);
        }
        return new(definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), locations);
    }

    internal static async Task<ChangeSpec[]> FormatAsync(Document document, int indentationSize, CancellationToken cancellation)
    {
        var source = await document.GetTextAsync(cancellation);
        var options = (await document.GetOptionsAsync(cancellation))
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
            .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, indentationSize)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, indentationSize)
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, Text.OfString(source.ToString()).PreferredNewline);
        var formatted = await Formatter.FormatAsync(document, options, cancellation);
        var changes = await formatted.GetTextChangesAsync(document, cancellation);
        return changes.OrderBy(static change => change.Span.Start)
            .Select(static change => new ChangeSpec(change.Span.Start, change.Span.End, change.NewText ?? "")).ToArray();
    }

    internal static async Task<LanguageIssue[]> DiagnosticsAsync(Document document, Func<Document, string?> documentKey,
        Func<Compilation, CancellationToken, OutputKind>? selectOutputKind, CancellationToken cancellation,
        IReadOnlyList<Diagnostic>? generatorDiagnostics = null)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellation);
        if (compilation is null) return [];
        if (selectOutputKind is not null) {
            var outputKind = selectOutputKind(compilation, cancellation);
            compilation = compilation.WithOptions(compilation.Options.WithOutputKind(outputKind));
        }
        var issues = new List<LanguageIssue>();
        var sources = new Dictionary<DocumentId, (SourceText Text, string Source)>();
        foreach (var diagnostic in compilation.GetDiagnostics(cancellation).Concat(generatorDiagnostics ?? [])) {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) continue;
            cancellation.ThrowIfCancellationRequested();
            var target = diagnostic.Location.IsInSource
                ? await PositionAsync(document.Project, diagnostic.Location, documentKey, cancellation, sources)
                : null;
            issues.Add(new(diagnostic.Id, diagnostic.GetMessage(), diagnostic.Severity == DiagnosticSeverity.Error, target));
        }
        return issues.OrderByDescending(static issue => issue.Error)
            .ThenBy(static issue => issue.Location?.Path, StringComparer.Ordinal)
            .ThenBy(static issue => issue.Location?.Start ?? -1)
            .ThenBy(static issue => issue.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<SourcePosition?> PositionAsync(Project project, Location location,
        Func<Document, string?> documentKey, CancellationToken cancellation,
        Dictionary<DocumentId, (SourceText Text, string Source)> sources)
    {
        cancellation.ThrowIfCancellationRequested();
        var document = await FindDocumentAsync(project, location.SourceTree!, cancellation);
        if (document is null) return null;

        if (!sources.TryGetValue(document.Id, out var source)) {
            var text = await document.GetTextAsync(cancellation);
            source = (text, text.ToString());
            sources.Add(document.Id, source);
        }
        var span = location.SourceSpan;
        var point = source.Text.Lines.GetLinePosition(span.Start);
        var key = documentKey(document);
        return new(
            key ?? "$generated:" + document.Id, document.FilePath ?? document.Name,
            span.Start, span.Length, point.Line + 1, point.Character + 1, source.Source, key is null);
    }

    private static async ValueTask<Document?> FindDocumentAsync(Project project, SyntaxTree sourceTree, CancellationToken cancellation)
    {
        var document = project.Solution.GetDocument(sourceTree);
        if (document is not null) return document;

        foreach (var candidate in project.Documents.Where(candidate => candidate.FilePath == sourceTree.FilePath)) {
            var candidateText = await candidate.GetTextAsync(cancellation);
            var sourceText = await sourceTree.GetTextAsync(cancellation);
            if (candidateText.ContentEquals(sourceText)) return candidate;
        }

        foreach (var generated in await project.GetSourceGeneratedDocumentsAsync(cancellation)) {
            var tree = await generated.GetSyntaxTreeAsync(cancellation);
            if (ReferenceEquals(tree, sourceTree)) return generated;
        }
        return null;
    }
}
