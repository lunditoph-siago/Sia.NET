using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Tags;
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
    private readonly Dictionary<string, (DocumentId Id, SourceText Text, bool Generated)> _documents = [];
    private AdhocWorkspace? _workspace;
    private Project? _project;
    private int _generatorVersion = -1;
    private GeneratorDriver? _generatorDriver;
    private bool _generationPending = true;
    private IReadOnlyList<Diagnostic> _generatorDiagnostics = [];
    private IReadOnlyList<MetadataReference>? _expandedReferenceProbe;
    private IReadOnlyList<MetadataReference>? _expandedReferences;
    private bool _disposed;
    private Task _cleanup = Task.CompletedTask;

    internal Func<string, string, CSharpQueryKind, CancellationToken, ValueTask<CSharpProjectSnapshot>>? CaptureSnapshot { get; set; }

    public Task<CompletionResult> QueryAsync(string documentId, string source, int position,
        CancellationToken cancellationToken = default)
        => WithDocumentAsync(documentId, source, async (document, snapshot, token) => {
            var service = CompletionService.GetService(document)
                ?? throw new InvalidOperationException("C# completion services are unavailable.");
            var cursor = snapshot.Projection?.ToProject(position).Position ?? Math.Clamp(position, 0, source.Length);
            var list = await service.GetCompletionsAsync(document, cursor, cancellationToken: token);
            if (list is null || list.ItemsList.Count == 0) return CompletionResult.Empty;
            var start = list.Span.Start;
            if (snapshot.Projection is { } projection
                && (!projection.TryToSource(document.FilePath!, list.Span.Start, list.Span.Length, out var id, out start)
                    || id != projection.ActiveId)) return CompletionResult.Empty;
            var root = await document.GetSyntaxRootAsync(token);
            var node = root?.FindToken(Math.Max(0, cursor - 1)).Parent;
            var local = node?.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault(variable =>
                variable.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax }
                && variable.Initializer is { } initializer && initializer.Span.Start <= cursor && cursor <= initializer.Span.End);
            if (local is not null && node is not null) {
                var model = await document.GetSemanticModelAsync(token);
                if (node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation => model?.GetOperation(invocation, token) is INameOfOperation)) local = null;
            }
            var items = list.ItemsList.Where(item => local is null || item.FilterText.TrimStart('@') != local.Identifier.ValueText
                || !item.Tags.Contains(WellKnownTags.Local)).Select(item => new CompletionCandidate(
                item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                item.DisplayText, start, start + list.Span.Length) { Item = item }).ToArray();
            return new CompletionResult(items) {
                Query = new(document, service, snapshot.Projection,
                    snapshot.Projection is null ? null : (text, caret, cancellation) => QueryAsync(documentId, text, caret, cancellation)),
                SuggestionMode = IsSuggestionMode(list.Span.Length, start, list.SuggestionModeItem is not null, source)
            };
        }, cancellationToken, position: position);

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

    private string? DocumentKey(Document document)
        => _documents.FirstOrDefault(entry => entry.Value.Id == document.Id && !entry.Value.Generated).Key;

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

    private async Task<T> WithDocumentAsync<T>(string documentId, string source,
        Func<Document, CSharpProjectSnapshot, CancellationToken, Task<T>> query, CancellationToken cancellationToken, bool expandReferences = false,
        int position = 0, CSharpQueryKind kind = CSharpQueryKind.Language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = cancellation.Token;
        var snapshot = CaptureSnapshot is { } capture
            ? await capture(documentId, source, kind, token)
            : new CSharpProjectSnapshot(documentId, [new(documentId, "Completion.cs", "Completion.cs", source)]);
        var probe = await references.GetReferencesAsync(snapshot.GlobalUsings + "\n"
            + string.Join('\n', snapshot.Documents.Select(static file => file.Source)), token);
        var previousProbe = _expandedReferenceProbe;
        var expanded = expandReferences && (previousProbe is null || !probe.SequenceEqual(previousProbe))
            ? await references.GetAllReferencesAsync(token) : null;
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            token.ThrowIfCancellationRequested();
            if (expanded is not null) { _expandedReferenceProbe = probe; _expandedReferences = expanded; }
            var metadata = _expandedReferenceProbe is not null && probe.SequenceEqual(_expandedReferenceProbe) ? _expandedReferences! : probe;
            var activeId = snapshot.Projection is { } projection
                ? snapshot.Documents.First(file => file.Path == projection.ToProject(position).Path).Id : snapshot.ActiveDocumentId;
            try {
                var document = await UpdateProjectAsync(snapshot, metadata, activeId, token);
                return await query(document, snapshot, token);
            }
            catch { _generationPending = true; throw; }
        }, token);
    }

    private static async Task<CompletionEdit?> GetChangeAsync(CompletionResult completion, CompletionCandidate candidate,
        string source, string newline, CancellationToken cancellationToken, char? character = null)
    {
        if (completion.Query is not { } query || candidate.Item is not { } item)
            return new([new(candidate.ReplaceStart, candidate.ReplaceEnd, candidate.InsertText)],
                candidate.ReplaceStart + candidate.InsertText.Length);
        var document = query.EditingDocument ?? query.Document;
        var edit = await query.Service.GetChangeAsync(document, item, character, cancellationToken);
        var position = edit.NewPosition ?? edit.TextChange.Span.Start + (edit.TextChange.NewText?.Length ?? 0);
        var (changes, caret) = NormalizeImportNewlines(edit.TextChanges, position, newline, cancellationToken);
        var result = query.Projection is { } projection ? projection.ToSource(query.Document.FilePath!, changes, caret, cancellationToken,
            (CompilationUnitSyntax?)await document.GetSyntaxRootAsync(cancellationToken))
            : new CompletionEdit([.. changes
            .Select(static change => new ChangeSpec(change.Span.Start, change.Span.End, change.NewText ?? ""))],
            caret);
        return result is { } change && character is { } commit
            ? change.WithCharacter(commit, source.Length, edit.IncludesCommitCharacter) : result;
    }

    private static (TextChange[] Changes, int Position) NormalizeImportNewlines(IReadOnlyList<TextChange> edits, int position,
        string newline, CancellationToken cancellation)
    {
        var changes = edits.OrderBy(static change => change.Span.Start).ToArray();
        var delta = 0;
        var caret = position;
        for (var index = 0; index < changes.Length; index++) {
            var change = changes[index];
            var text = change.NewText ?? "";
            var end = change.Span.Start + delta + text.Length;
            delta += text.Length - change.Span.Length;
            if (change.Span.Length != 0 || end > position || !text.Contains('\n') || !text.TrimStart().StartsWith("using ", StringComparison.Ordinal)) continue;
            var root = CSharpSyntaxTree.ParseText(text, CSharpLanguageOptions.Parse, cancellationToken: cancellation).GetCompilationUnitRoot(cancellation);
            if (root.ContainsDiagnostics || root.Usings.Count == 0 || root.Members.Count != 0 || root.Externs.Count != 0
                || root.DescendantTrivia().Any(static trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))) continue;
            var normalized = root.ReplaceTrivia(root.DescendantTrivia().Where(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)),
                (_, _) => SyntaxFactory.EndOfLine(newline)).ToFullString();
            changes[index] = new(change.Span, normalized);
            caret += normalized.Length - text.Length;
        }
        return (changes, caret);
    }

    internal static char[] CommitCharacters(CompletionItem item, CompletionRules rules)
    {
        var characters = rules.DefaultCommitCharacters.ToHashSet();
        foreach (var rule in item.Rules.CommitCharacterRules) {
            switch (rule.Kind) {
                case CharacterSetModificationKind.Replace: characters.Clear(); characters.UnionWith(rule.Characters); break;
                case CharacterSetModificationKind.Add: characters.UnionWith(rule.Characters); break;
                case CharacterSetModificationKind.Remove: characters.ExceptWith(rule.Characters); break;
            }
        }
        return characters.Where(character => !char.IsControl(character) && !Filtered(character)).Order().ToArray();

        bool Filtered(char character)
        {
            var filtered = CompletionIdentifier.IsCharacter(character);
            foreach (var rule in item.Rules.FilterCharacterRules) {
                var contains = rule.Characters.Contains(character);
                filtered = rule.Kind switch {
                    CharacterSetModificationKind.Replace => contains,
                    CharacterSetModificationKind.Add => filtered || contains,
                    CharacterSetModificationKind.Remove => filtered && !contains,
                    _ => filtered
                };
            }
            return filtered;
        }
    }

    internal static Task<CompletionResult> PrepareAsync(CompletionResult completion, string source, int position,
        CancellationToken cancellationToken)
        => PrepareAsync(completion, source, position, cancellationToken, static (_, _) => true);

    internal static Task<CompletionResult> PrepareWindowAsync(CompletionResult completion, string source, int position,
        int selectedIndex, CancellationToken cancellationToken)
        => PrepareAsync(completion, source, position, cancellationToken,
            (index, character) => index == selectedIndex && character is '.' or '(' or ';');

    private static async Task<CompletionResult> PrepareAsync(CompletionResult completion, string source, int position,
        CancellationToken cancellationToken, Func<int, char, bool> prepareCharacter)
    {
        if (completion.Query is { Projection: { } projection, Refresh: { } refresh } && projection.ActiveSource.Source != source) {
            var fresh = await refresh(source, position, cancellationToken);
            completion = fresh with { Items = completion.Items.SelectMany(original => fresh.Items.Where(original.SameIdentity).Take(1)).ToArray() };
        }
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            CompletionList? fresh = null;
            var newline = Text.OfString(source).PreferredNewline;
            var query = completion.Query;
            if (query is not null && (query.Projection?.ActiveSource.Source ?? (await query.Document.GetTextAsync(cancellationToken)).ToString()) != source) {
                var document = query.Document.WithText(SourceText.From(source));
                fresh = await query.Service.GetCompletionsAsync(document, position, cancellationToken: cancellationToken);
                if (fresh is null) return CompletionResult.Empty;
                query = new(document, query.Service);
            }
            if (query is { Projection: not null, EditingDocument: null })
                query = query with { EditingDocument = await CSharpSourceProjection.PrepareEditingDocumentAsync(query.Document, cancellationToken) };
            var prepared = new List<CompletionCandidate>(completion.Items.Count);
            var rules = query?.Service.GetRules();
            for (var index = 0; index < completion.Items.Count; index++) {
                var original = completion.Items[index];
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = original;
                if (fresh is not null && original.Item is { } item) {
                    var current = fresh.ItemsList.FirstOrDefault(next => original.SameIdentity(original with { Item = next }));
                    if (current is null) continue;
                    candidate = candidate with { Item = current, ReplaceStart = fresh.Span.Start, ReplaceEnd = fresh.Span.End };
                }
                var context = completion with { Query = query };
                var reuse = query is not null && ReferenceEquals(candidate.PreparedQuery, query);
                var edit = reuse ? candidate.Edit : null;
                edit ??= await GetChangeAsync(context, candidate, source, newline, cancellationToken);
                if (edit is null) continue;
                Dictionary<char, CompletionEdit>? commits = null;
                var complete = true;
                if (candidate.Item is { } itemWithRules && rules is not null) {
                    commits = reuse && candidate.CommitEdits is { } previous ? new(previous) : [];
                    var characters = CommitCharacters(itemWithRules, rules);
                    complete = characters.All(character => commits.ContainsKey(character) || prepareCharacter(index, character));
                    foreach (var character in characters.Where(character => !commits.ContainsKey(character) && prepareCharacter(index, character))) {
                        if (await GetChangeAsync(context, candidate, source, newline, cancellationToken, character) is { } committed)
                            commits.Add(character, committed);
                    }
                }
                prepared.Add(candidate with { Edit = edit, CommitEdits = commits, CommitEditsComplete = complete, PreparedQuery = query });
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
        if (_project.CompilationOptions!.OutputKind != snapshot.OutputKind)
            _project = _project.WithCompilationOptions(_project.CompilationOptions.WithOutputKind(snapshot.OutputKind));
        var generatorChanged = _generatorVersion != DynamicAssemblyRegistry.Version;
        if (generatorChanged) {
            _generatorVersion = DynamicAssemblyRegistry.Version;
            _generatorDriver = CSharpGeneratorDriver.Create(GeneratorPipeline.GetGenerators(), parseOptions: CSharpLanguageOptions.Parse);
        }
        if (!_project.MetadataReferences.SequenceEqual(references)) _project = _project.WithMetadataReferences(references);
        var files = snapshot.Documents.Append(new CSharpSourceDocument("$global-usings", "GlobalUsings.g.cs",
            "GlobalUsings.g.cs", snapshot.GlobalUsings, false)).ToArray();
        await SynchronizeDocumentsAsync(files, generated: false, cancellationToken);
        _generationPending |= generatorChanged || _project != previous;
        if (_generationPending) {
            var input = _project.RemoveDocuments([.. _documents.Values.Where(static entry => entry.Generated).Select(static entry => entry.Id)]);
            var compilation = await input.GetCompilationAsync(cancellationToken)
                ?? throw new InvalidOperationException("The C# project could not be compiled.");
            _generatorDriver = _generatorDriver!.RunGenerators(compilation, cancellationToken);
            var generated = _generatorDriver.GetRunResult();
            _generatorDiagnostics = generated.Diagnostics;
            await SynchronizeDocumentsAsync(generated.GeneratedTrees.Select(tree => new CSharpSourceDocument(
                "$generated:" + tree.FilePath, tree.FilePath, tree.FilePath, tree.GetText(cancellationToken).ToString(), false)),
                generated: true, cancellationToken);
            _generationPending = false;
        }
        var activeDocument = _documents[activeId];
        if (_project != previous && ReferenceEquals(activeBefore.Text, activeDocument.Text))
            _project = _project.GetDocument(activeDocument.Id)!.WithText(SourceText.From(activeDocument.Text.ToString())).Project;
        if (!_workspace.TryApplyChanges(_project.Solution))
            throw new InvalidOperationException("The C# project could not be synchronized.");
        _project = _workspace.CurrentSolution.GetProject(_project.Id)!;
        return _project.GetDocument(_documents[activeId].Id)!;
    }

    private async Task SynchronizeDocumentsAsync(IEnumerable<CSharpSourceDocument> sources, bool generated, CancellationToken cancellationToken)
    {
        var files = sources.ToArray();
        var retained = files.Select(static file => file.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _documents.Where(entry => entry.Value.Generated == generated && !retained.Contains(entry.Key)).Select(static entry => entry.Key).ToArray()) {
            _project = _project!.RemoveDocument(_documents[id].Id);
            _documents.Remove(id);
        }
        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_documents.TryGetValue(file.Id, out var existing)) {
                var document = _project!.AddDocument(file.Path, SourceText.From(file.Source), filePath: file.Path);
                _project = document.Project;
                _documents.Add(file.Id, (document.Id, await document.GetTextAsync(cancellationToken), generated));
            }
            else if (!existing.Text.ContentEquals(SourceText.From(file.Source))) {
                var before = existing.Text.ToString();
                var start = 0;
                while (start < before.Length && start < file.Source.Length && before[start] == file.Source[start]) start++;
                var end = before.Length;
                var newEnd = file.Source.Length;
                while (end > start && newEnd > start && before[end - 1] == file.Source[newEnd - 1]) { end--; newEnd--; }
                var text = existing.Text.WithChanges(new TextChange(TextSpan.FromBounds(start, end), file.Source[start..newEnd]));
                _project = _project!.GetDocument(existing.Id)!.WithText(text).WithFilePath(file.Path).Project;
                _documents[file.Id] = (existing.Id, text, generated);
            }
            else if (_project!.GetDocument(existing.Id)!.FilePath != file.Path) {
                _project = _project.GetDocument(existing.Id)!.WithFilePath(file.Path).Project;
            }
        }
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
            _generatorDriver = null;
            _generatorDiagnostics = [];
            _expandedReferenceProbe = null;
            _expandedReferences = null;
            _lifetime.Dispose();
            return true;
        }, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() { Dispose(); await _cleanup; }
}

internal sealed record CSharpProjectSnapshot(string ActiveDocumentId, IReadOnlyList<CSharpSourceDocument> Documents)
{
    internal OutputKind OutputKind { get; init; } = Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary;
    internal Func<Compilation, CancellationToken, OutputKind>? SelectOutputKind { get; init; }
    internal string GlobalUsings { get; init; } = NotebookLanguageContext.GlobalUsings;
    internal CSharpSourceProjection? Projection { get; init; }

    internal static CSharpProjectSnapshot ForNotebook(string activeId, IReadOnlyList<NotebookProgramCell> cells, CancellationToken cancellationToken = default,
        IReadOnlySet<string>? readOnly = null)
    {
        var program = NotebookProgramBuilder.Build(cells, cancellationToken);
        var originals = cells.SelectMany(static cell => cell.Files).Select(file =>
            new CSharpSourceDocument(file.Id, file.Name, file.Name, file.Source, readOnly?.Contains(file.Id) != true)).ToArray();
        return new(activeId, [.. program.CompilationSources, new("$render-support", "Notebook/Render.g.cs", "Render.g.cs",
            NotebookProgramBuilder.BuildStandaloneRenderSupport(), false)]) {
            OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            GlobalUsings = NotebookLanguageContext.GetGlobalUsings(program.NeedsWrapperUsing),
            Projection = new(activeId, originals, program.SourceMappings)
        };
    }
}
internal sealed record CompletionQuery(Document Document, CompletionService Service, CSharpSourceProjection? Projection = null,
    Func<string, int, CancellationToken, Task<CompletionResult>>? Refresh = null)
{
    internal Document? EditingDocument { get; init; }
}
internal readonly record struct CompletionEdit(ChangeSpec[] Changes, int Position)
{
    internal CompletionEdit WithCharacter(char character, int sourceLength, bool includesCharacter = false)
    {
        if (includesCharacter) return this;
        var changes = ChangeSet.Of(Changes, sourceLength);
        var insertion = ChangeSet.Of([new(Position, Position, character.ToString())], changes.MapPos(sourceLength, 1)!.Value);
        return new(ChangeSet.Compose(changes, insertion).ToSpecs(), Position + 1);
    }
}
