using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class CSharpCompletionProvider(ICompilationReferenceResolver references) : IDisposable, IAsyncDisposable
{
    private static readonly Lazy<MefHostServices> _services = new(() => MefHostServices.Create(
        MefHostServices.DefaultAssemblies
            .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"))
            .Add(Assembly.Load("Microsoft.CodeAnalysis.Features"))
            .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")).Distinct()));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (DocumentId Id, SourceText Text)> _documents = [];
    private AdhocWorkspace? _workspace;
    private Project? _project;
    private int _generatorVersion = -1;
    private bool _disposed;
    private Task _cleanup = Task.CompletedTask;

    internal Func<string, string, CancellationToken, ValueTask<CSharpProjectSnapshot>>? CaptureSnapshot { get; set; }

    public Task<CompletionResult> QueryAsync(string documentId, string source, int position,
        CancellationToken cancellationToken = default)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var service = CompletionService.GetService(document)
                ?? throw new InvalidOperationException("C# completion services are unavailable.");
            var cursor = Math.Clamp(position, 0, source.Length);
            var list = await service.GetCompletionsAsync(document, cursor, cancellationToken: token);
            if (list is null || list.ItemsList.Count == 0) return CompletionResult.Empty;
            var start = list.Span.Start;
            var items = list.ItemsList.Select(item => new CompletionCandidate(
                item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                item.DisplayText, start, start + list.Span.Length) { Item = item }).ToArray();
            return new CompletionResult(items) {
                Query = new(document, service),
                SuggestionMode = IsSuggestionMode(list.Span.Length, start, list.SuggestionModeItem is not null, source)
            };
        }, cancellationToken);

    internal Task<SignatureHelp?> QuerySignatureAsync(string documentId, string source, int position,
        CancellationToken cancellationToken = default)
        => WithDocumentAsync(documentId, source, (document, snapshot, token) =>
            CSharpSignatureHelp.QueryAsync(document, Math.Clamp(position, 0, source.Length), token), cancellationToken);

    private async Task<T> WithDocumentAsync<T>(string documentId, string source,
        Func<Document, CSharpProjectSnapshot, CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = cancellation.Token;
        var snapshot = CaptureSnapshot is { } capture
            ? await capture(documentId, source, token)
            : new CSharpProjectSnapshot(documentId, [new(documentId, "Completion.cs", "Completion.cs", source)]);
        var metadata = await references.GetReferencesAsync(snapshot.GlobalUsings + "\n"
            + string.Join('\n', snapshot.Documents.Select(static file => file.Source)), token);
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            token.ThrowIfCancellationRequested();
            var document = await UpdateProjectAsync(snapshot, metadata, snapshot.ActiveDocumentId, token);
            return await query(document, snapshot, token);
        }, token);
    }

    private static async Task<CompletionEdit?> GetChangeAsync(CompletionResult completion, CompletionCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (completion.Query is not { } query || candidate.Item is not { } item)
            return new([new(candidate.ReplaceStart, candidate.ReplaceEnd, candidate.InsertText)],
                candidate.ReplaceStart + candidate.InsertText.Length);
        var edit = await query.Service.GetChangeAsync(query.Document, item, cancellationToken: cancellationToken);
        var position = edit.NewPosition ?? edit.TextChange.Span.Start + (edit.TextChange.NewText?.Length ?? 0);
        return new CompletionEdit([.. edit.TextChanges.OrderBy(static change => change.Span.Start)
            .Select(static change => new ChangeSpec(change.Span.Start, change.Span.End, change.NewText ?? ""))],
            position);
    }

    internal static async Task<CompletionResult> PrepareAsync(CompletionResult completion, string source, int position,
        CancellationToken cancellationToken)
    {
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            CompletionList? fresh = null;
            var query = completion.Query;
            if (query is not null && (await query.Document.GetTextAsync(cancellationToken)).ToString() != source) {
                var document = query.Document.WithText(SourceText.From(source));
                fresh = await query.Service.GetCompletionsAsync(document, position, cancellationToken: cancellationToken);
                if (fresh is null) return CompletionResult.Empty;
                query = new(document, query.Service);
            }
            var prepared = new List<CompletionCandidate>(completion.Items.Count);
            foreach (var original in completion.Items) {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = original;
                if (fresh is not null && original.Item is { } item) {
                    var current = fresh.ItemsList.FirstOrDefault(next => original.SameIdentity(original with { Item = next }));
                    if (current is null) continue;
                    candidate = candidate with { Item = current, ReplaceStart = fresh.Span.Start, ReplaceEnd = fresh.Span.End };
                }
                var context = completion with { Query = query };
                if (await GetChangeAsync(context, candidate, cancellationToken) is { } edit)
                    prepared.Add(candidate with { Edit = edit, PreparedQuery = query });
            }
            return new CompletionResult(prepared) { Query = query, SuggestionMode = fresh is null ? completion.SuggestionMode : IsSuggestionMode(fresh, source) };
        }, cancellationToken);
    }

    private static bool IsSuggestionMode(CompletionList list, string source)
        => IsSuggestionMode(list.Span.Length, list.Span.Start, list.SuggestionModeItem is not null, source);

    private static bool IsSuggestionMode(int length, int start, bool suggestion, string source)
        => suggestion || length == 0 && (start == 0
            || source[start - 1] != '.' && !(start >= 2 && source.AsSpan(start - 2, 2).SequenceEqual("->")));

    internal static Task<string> GetDescriptionAsync(CompletionResult completion, CompletionCandidate candidate,
        CancellationToken cancellationToken)
        => (candidate.PreparedQuery ?? completion.Query) is { } query && candidate.Item is { } item
            ? CSharpCompilationEngine.RunRoslynAsync(async () =>
                (await query.Service.GetDescriptionAsync(query.Document, item, cancellationToken))?.Text ?? "", cancellationToken)
            : Task.FromResult("");

    private async Task<Document> UpdateProjectAsync(CSharpProjectSnapshot snapshot,
        IReadOnlyList<MetadataReference> references, string activeId, CancellationToken cancellationToken)
    {
        var previous = _project;
        _documents.TryGetValue(activeId, out var activeBefore);
        _workspace ??= new(_services.Value);
        _project ??= _workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "Editor", "Editor", LanguageNames.CSharp, parseOptions: CSharpLanguageOptions.Parse,
            compilationOptions: CSharpLanguageOptions.Library));
        var options = CSharpLanguageOptions.Library.WithOutputKind(snapshot.OutputKind);
        if (!_project.CompilationOptions!.Equals(options)) _project = _project.WithCompilationOptions(options);
        if (_generatorVersion != DynamicAssemblyRegistry.Version) {
            _generatorVersion = DynamicAssemblyRegistry.Version;
            _project = _project.WithAnalyzerReferences([new EditorGeneratorReference(_generatorVersion)]);
        }
        if (!_project.MetadataReferences.SequenceEqual(references)) _project = _project.WithMetadataReferences(references);
        var files = snapshot.Documents.Append(new CSharpSourceDocument("$global-usings", "GlobalUsings.g.cs",
            "GlobalUsings.g.cs", snapshot.GlobalUsings, false)).ToArray();
        var retained = files.Select(static file => file.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _documents.Keys.Where(id => !retained.Contains(id)).ToArray()) {
            _project = _project.RemoveDocument(_documents[id].Id);
            _documents.Remove(id);
        }
        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_documents.TryGetValue(file.Id, out var existing)) {
                var document = _project.AddDocument(file.Path, SourceText.From(file.Source), filePath: file.Path);
                _project = document.Project;
                _documents.Add(file.Id, (document.Id, await document.GetTextAsync(cancellationToken)));
            }
            else if (!existing.Text.ContentEquals(SourceText.From(file.Source))) {
                var before = existing.Text.ToString();
                var start = 0;
                while (start < before.Length && start < file.Source.Length && before[start] == file.Source[start]) start++;
                var end = before.Length;
                var newEnd = file.Source.Length;
                while (end > start && newEnd > start && before[end - 1] == file.Source[newEnd - 1]) { end--; newEnd--; }
                var text = existing.Text.WithChanges(new TextChange(TextSpan.FromBounds(start, end), file.Source[start..newEnd]));
                _project = _project.GetDocument(existing.Id)!.WithText(text).WithFilePath(file.Path).Project;
                _documents[file.Id] = (existing.Id, text);
            }
            else if (_project.GetDocument(existing.Id)!.FilePath != file.Path) {
                _project = _project.GetDocument(existing.Id)!.WithFilePath(file.Path).Project;
            }
        }
        var activeDocument = _documents[activeId];
        if (_project != previous && ReferenceEquals(activeBefore.Text, activeDocument.Text))
            _project = _project.GetDocument(activeDocument.Id)!.WithText(SourceText.From(activeDocument.Text.ToString())).Project;
        if (!_workspace.TryApplyChanges(_project.Solution))
            throw new InvalidOperationException("The C# project could not be synchronized.");
        _project = _workspace.CurrentSolution.GetProject(_project.Id)!;
        return _project.GetDocument(_documents[activeId].Id)!;
    }

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
            _lifetime.Dispose();
            return true;
        }, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() { Dispose(); await _cleanup; }
}

internal sealed record CSharpProjectSnapshot(string ActiveDocumentId, IReadOnlyList<CSharpSourceDocument> Documents)
{
    internal OutputKind OutputKind { get; init; } = Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary;
    internal string GlobalUsings { get; init; } = NotebookLanguageContext.GlobalUsings;
}
internal sealed record CompletionQuery(Document Document, CompletionService Service);
internal readonly record struct CompletionEdit(ChangeSpec[] Changes, int Position);
