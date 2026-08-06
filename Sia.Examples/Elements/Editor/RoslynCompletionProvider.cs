using System.Threading.Channels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class RoslynCompletionProvider : IEditorCompletionProvider, IDisposable
{
    private const string DocumentName = "Completion.cs";

    private static readonly Lazy<HostServices> Host = new(() =>
        Microsoft.CodeAnalysis.Host.Mef.MefHostServices.Create(
            Microsoft.CodeAnalysis.Host.Mef.MefHostServices.DefaultAssemblies
                .Append(typeof(CompletionService).Assembly)
                .Append(System.Reflection.Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"))));

    private readonly IMetadataReferenceProvider _referenceProvider;

    private readonly Channel<QueryRequest> _requests = Channel.CreateUnbounded<QueryRequest>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;

    private AdhocWorkspace? _workspace;
    private DocumentId? _documentId;
    private SourceText? _lastText;
    private IReadOnlyList<MetadataReference>? _appliedReferences;

    private CompletionList? _cachedList;
    private int _cachedAnchor = -1;

    private readonly record struct QueryRequest(
        string Source, int Position, CancellationToken CancellationToken,
        TaskCompletionSource<CompletionQueryResult> Completion);

    public RoslynCompletionProvider(IMetadataReferenceProvider referenceProvider)
    {
        _referenceProvider = referenceProvider;
        _worker = Task.Run(RunWorkerAsync);
    }

    public Task<CompletionQueryResult> QueryAsync(
        string source, int position, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CompletionQueryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.Writer.TryWrite(new QueryRequest(source, position, cancellationToken, completion))) {
            completion.TrySetException(new ObjectDisposedException(nameof(RoslynCompletionProvider)));
        }
        return completion.Task;
    }

    private async Task RunWorkerAsync()
    {
        await foreach (var request in _requests.Reader.ReadAllAsync()) {
            if (request.CancellationToken.IsCancellationRequested) {
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }
            try {
                var result = await QueryCoreAsync(request.Source, request.Position, request.CancellationToken)
                    .ConfigureAwait(false);
                request.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested) {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            catch (Exception ex) {
                request.Completion.TrySetException(ex);
            }
        }
    }

    private async Task<CompletionQueryResult> QueryCoreAsync(
        string source, int position, CancellationToken cancellationToken)
    {
        var document = await SyncDocumentAsync(source, cancellationToken).ConfigureAwait(false);

        var service = CompletionService.GetService(document);
        if (service is null) {
            return CompletionQueryResult.Empty;
        }

        position = Math.Clamp(position, 0, source.Length);

        CompletionList? list;
        if (_cachedList is not null && IsSameSession(source, position)) {
            list = _cachedList;
        } else {
            list = await service.GetCompletionsAsync(document, position, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (list is null || list.ItemsList.Count == 0) {
                _cachedList = null;
                _cachedAnchor = -1;
                return CompletionQueryResult.Empty;
            }
            _cachedList = list;
            _cachedAnchor = list.Span.Start;
        }

        var filterEnd = Math.Clamp(position, list.Span.Start, source.Length);
        var filterText = filterEnd > list.Span.Start ? source[list.Span.Start..filterEnd] : "";
        var filtered = filterText.Length == 0
            ? list.ItemsList
            : (IReadOnlyList<CompletionItem>)service.FilterItems(document, [.. list.ItemsList], filterText);

        var items = new List<CompletionCandidate>(Math.Min(filtered.Count, 20));
        foreach (var item in filtered.OrderBy(i => i.SortText, StringComparer.Ordinal).Take(20)) {
            var change = await service.GetChangeAsync(document, item, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var replaceEnd = Math.Max(change.TextChange.Span.End, position);
            items.Add(new CompletionCandidate(
                item.DisplayText, change.TextChange.NewText ?? "",
                change.TextChange.Span.Start, replaceEnd));
        }

        return new CompletionQueryResult(items);
    }

    private bool IsSameSession(string source, int position)
    {
        if (_cachedAnchor < 0 || _cachedAnchor > position || _cachedAnchor > source.Length) {
            return false;
        }
        if (_cachedAnchor > 0 && IsIdentifierChar(source[_cachedAnchor - 1])) {
            return false;
        }
        for (var i = _cachedAnchor; i < position; i++) {
            if (!IsIdentifierChar(source[i])) return false;
        }
        return true;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private async Task<Document> SyncDocumentAsync(string source, CancellationToken cancellationToken)
    {
        var references = await _referenceProvider.GetReferencesAsync(source).ConfigureAwait(false);

        if (_workspace is null) {
            var workspace = new AdhocWorkspace(Host.Value);
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId, DocumentName);
            var text = SourceText.From(source);

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "Completion", "Completion", LanguageNames.CSharp)
                .WithProjectMetadataReferences(projectId, references)
                .AddDocument(documentId, DocumentName, text);
            workspace.TryApplyChanges(solution);

            _workspace = workspace;
            _documentId = documentId;
            _lastText = text;
            _appliedReferences = references;
            return workspace.CurrentSolution.GetDocument(documentId)!;
        }

        var existingId = _documentId!;
        var doc = _workspace.CurrentSolution.GetDocument(existingId)!;

        if (!ReferenceEquals(_appliedReferences, references)) {
            var solution = doc.Project.Solution.WithProjectMetadataReferences(doc.Project.Id, references);
            _workspace.TryApplyChanges(solution);
            _appliedReferences = references;
            doc = _workspace.CurrentSolution.GetDocument(existingId)!;
            _cachedList = null;
            _cachedAnchor = -1;
        }

        var oldString = _lastText!.ToString();
        if (oldString != source) {
            var newText = _lastText.WithChanges(DiffText(oldString, source));
            var solution = doc.Project.Solution.WithDocumentText(existingId, newText);
            _workspace.TryApplyChanges(solution);
            _lastText = newText;
            doc = _workspace.CurrentSolution.GetDocument(existingId)!;
        }

        return doc;
    }

    private static TextChange DiffText(string oldString, string newText)
    {
        var prefix = 0;
        var minLen = Math.Min(oldString.Length, newText.Length);
        while (prefix < minLen && oldString[prefix] == newText[prefix]) prefix++;

        var oldRemaining = oldString.Length - prefix;
        var newRemaining = newText.Length - prefix;
        var maxSuffix = Math.Min(oldRemaining, newRemaining);
        var suffix = 0;
        while (suffix < maxSuffix &&
               oldString[oldString.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) {
            suffix++;
        }

        var span = TextSpan.FromBounds(prefix, oldString.Length - suffix);
        var replacement = newText.Substring(prefix, newRemaining - suffix);
        return new TextChange(span, replacement);
    }

    public void Dispose()
    {
        _requests.Writer.TryComplete();
        _workspace?.Dispose();
    }
}
