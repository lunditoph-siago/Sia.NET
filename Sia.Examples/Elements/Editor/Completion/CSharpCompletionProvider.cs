using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed partial class CSharpCompletionProvider(ICompilationReferenceResolver references) : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private (string Id, Document Document, int Position, CSharpProjectSnapshot Snapshot, CompletionResult Result)? _completionCache;
    private bool _disposed;
    private Task _cleanup = Task.CompletedTask;

    internal Func<string, string, CSharpQueryKind, CancellationToken, ValueTask<CSharpProjectSnapshot>>? CaptureSnapshot { get; set; }

    public Task<CompletionResult> QueryAsync(string documentId, string source, int position,
        CancellationToken cancellationToken = default)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var service = CompletionService.GetService(document)
                ?? throw new InvalidOperationException("C# completion services are unavailable.");
            var cursor = snapshot.Projection?.ToProject(position).Position ?? Math.Clamp(position, 0, source.Length);
            if (_completionCache is { } cached && cached.Id == documentId && cached.Position == position
                && ReferenceEquals(cached.Document, document) && cached.Snapshot.ActiveDocumentId == snapshot.ActiveDocumentId
                && cached.Snapshot.Documents.SequenceEqual(snapshot.Documents)
                && (cached.Snapshot.Projection is null ? snapshot.Projection is null
                    : snapshot.Projection is { } current && cached.Snapshot.Projection.HasSameContext(current))) return cached.Result;
            _completionCache = null;
            var list = await service.GetCompletionsAsync(document, cursor, cancellationToken: token);
            if (list is null || list.ItemsList.Count == 0) return CompletionResult.Empty;
            var start = list.Span.Start;
            if (snapshot.Projection is { } projection
                && (!projection.TryToSource(document.FilePath!, list.Span.Start, list.Span.Length, out var id, out start)
                    || id != projection.ActiveId)) return CompletionResult.Empty;
            var local = await FindInitializingLocalAsync(document, cursor, token);
            var items = list.ItemsList.Where(item => local is null || item.FilterText.TrimStart('@') != local.Identifier.ValueText
                || !item.Tags.Contains(WellKnownTags.Local)).Select(item => new CompletionCandidate(
                item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                item.DisplayText, start, start + list.Span.Length) { Item = item.WithTags(item.Tags.ToArray().ToImmutableArray()) }).ToArray();
            var result = new CompletionResult(items) {
                Query = new(document, service, snapshot.Projection,
                    (text, caret, cancellation) => QueryAsync(documentId, text, caret, cancellation)),
                SuggestionMode = IsSuggestionMode(list.Span.Length, start, list.SuggestionModeItem is not null, source)
            };
            _completionCache = (documentId, document, position, snapshot, result);
            return result;
        }, cancellationToken, position: position);

    private static async ValueTask<VariableDeclaratorSyntax?> FindInitializingLocalAsync(
        Document document, int position, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation);
        var node = root?.FindToken(Math.Max(0, position - 1)).Parent;
        var local = node?.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault(variable =>
            variable.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax }
            && variable.Initializer is { } initializer && initializer.Span.Start <= position && position <= initializer.Span.End);
        if (local is null || node is null) return null;

        var model = await document.GetSemanticModelAsync(cancellation);
        var insideNameOf = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .Any(invocation => model?.GetOperation(invocation, cancellation) is INameOfOperation);
        return insideNameOf ? null : local;
    }

    internal Task<SignatureHelp?> QuerySignatureAsync(string documentId, string source, int position,
        CancellationToken cancellationToken = default)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var result = await CSharpSignatureHelp.QueryAsync(document, snapshot.Projection?.ToProject(position).Position ?? Math.Clamp(position, 0, source.Length), token);
            if (result is null || snapshot.Projection is not { } projection) return result;
            return result with { Start = projection.TryToSource(document.FilePath!, result.Start, 0, out var id, out var start) && id == projection.ActiveId ? start : 0 };
        }, cancellationToken, position: position);

    internal Task<SymbolDefinition?> QueryDefinitionAsync(string documentId, string source, int position, CancellationToken cancellationToken)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var result = await CSharpDocumentTools.DefinitionAsync(document, snapshot.Projection?.ToProject(position).Position ?? position, DocumentKey, token);
            return result is null || snapshot.Projection is null ? result
                : result with { Locations = result.Locations.Select(snapshot.Projection.ToSource).Distinct().ToArray() };
        }, cancellationToken, position: position);

    internal Task<ChangeSpec[]> FormatAsync(string documentId, string source, int indentationSize, CancellationToken cancellationToken)
        => WithDocumentAsync(documentId, source, (document, snapshot, token) => CSharpDocumentTools.FormatAsync(
            snapshot.Projection is null ? document : document.WithText(SourceText.From(source)), indentationSize, token), cancellationToken);

    internal Task<SymbolReferences?> QueryReferencesAsync(string documentId, string source, int position, CancellationToken cancellationToken)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var result = await CSharpSymbolReferences.QueryAsync(document, snapshot.Projection?.ToProject(position).Position ?? position, DocumentKey, token);
            return result is null || snapshot.Projection is null ? result : result with {
                Occurrences = result.Occurrences.Select(occurrence => occurrence with { Location = snapshot.Projection.ToSource(occurrence.Location) })
                    .DistinctBy(static occurrence => (occurrence.Location.DocumentId, occurrence.Location.Start, occurrence.Location.Length, occurrence.Definition)).ToArray()
            };
        }, cancellationToken, position: position, kind: CSharpQueryKind.References);

    internal Task<RenamePlan> PrepareRenameAsync(string documentId, string source, int position, string name, CancellationToken cancellationToken)
        => WithDocumentAsync(documentId, source, (document, snapshot, token) => CSharpRename.PrepareAsync(document,
            snapshot.Projection?.ToProject(position).Position ?? position, name, DocumentKey, snapshot, token),
            cancellationToken, position: position, kind: CSharpQueryKind.References);

    internal Task<RenameTarget> QueryRenameTargetAsync(string documentId, string source, int position, CancellationToken cancellationToken)
        => WithDocumentAsync(documentId, source, (document, snapshot, token) => CSharpRename.TargetAsync(document,
            snapshot.Projection?.ToProject(position).Position ?? position, token), cancellationToken, position: position,
            kind: CSharpQueryKind.References);

    internal async Task<LanguageIssue[]> QueryDiagnosticsAsync(string documentId, string source, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Task<LanguageIssue[]> Query(bool expand) => WithDocumentAsync(documentId, source,
            async (document, snapshot, token) => {
                var issues = await CSharpDocumentTools.DiagnosticsAsync(document, DocumentKey, snapshot.SelectOutputKind, token, _generatorDiagnostics);
                return snapshot.Projection is null ? issues : issues.Select(issue => issue.Location is { } location
                    ? issue with { Location = snapshot.Projection.ToSource(location) } : issue).ToArray();
            }, cancellation.Token, expand);
        var issues = await Query(false);
        cancellation.Token.ThrowIfCancellationRequested();
        return issues.Any(static issue => issue.Error && CSharpCompilationEngine.IsMissingReference(issue.Id)) ? await Query(true) : issues;
    }

    internal async Task<DocumentSymbol[]> QuerySymbolsAsync(string documentId, string source, bool workspace,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = cancellation.Token;
        var snapshot = CaptureSnapshot is { } capture
            ? await capture(documentId, source, workspace ? CSharpQueryKind.WorkspaceSymbols : CSharpQueryKind.DocumentSymbols, token)
            : new CSharpProjectSnapshot(documentId, [new(documentId, "Completion.cs", "Completion.cs", source)]);
        return await CSharpCompilationEngine.RunRoslynAsync(() => CSharpDocumentSymbols.Read(
            (snapshot.Projection?.Sources ?? snapshot.Documents).Where(file => workspace || file.Id == snapshot.ActiveDocumentId), token), token);
    }

    private static bool IsSuggestionMode(int length, int start, bool suggestion, string source)
        => suggestion || length == 0 && (start == 0
            || source[start - 1] != '.' && !(start >= 2 && source.AsSpan(start - 2, 2).SequenceEqual("->")));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        CaptureSnapshot = null;
        _cleanup = CSharpCompilationEngine.RunRoslynAsync(() => {
            _workspace?.Dispose();
            _documents.Clear();
            _project = null;
            _generatorDriver = null;
            _generatorDiagnostics = [];
            _expandedReferenceProbe = null;
            _expandedReferences = null;
            _completionCache = null;
            _lifetime.Dispose();
            return true;
        }, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() { Dispose(); await _cleanup; }
}
