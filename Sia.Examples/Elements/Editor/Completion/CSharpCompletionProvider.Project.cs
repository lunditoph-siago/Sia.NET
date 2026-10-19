using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed partial class CSharpCompletionProvider
{
    private static readonly Lazy<MefHostServices> _services = new(() => MefHostServices.Create(
        MefHostServices.DefaultAssemblies
            .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"))
            .Add(Assembly.Load("Microsoft.CodeAnalysis.Features"))
            .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")).Distinct()));
    private readonly Dictionary<string, (DocumentId Id, SourceText Text, bool Generated)> _documents = [];
    private AdhocWorkspace? _workspace;
    private Project? _project;
    private int _generatorVersion = -1;
    private GeneratorDriver? _generatorDriver;
    private bool _generationPending = true;
    private IReadOnlyList<Diagnostic> _generatorDiagnostics = [];
    private IReadOnlyList<MetadataReference>? _expandedReferenceProbe;
    private IReadOnlyList<MetadataReference>? _expandedReferences;

    private string? DocumentKey(Document document)
        => _documents.FirstOrDefault(entry => entry.Value.Id == document.Id && !entry.Value.Generated).Key;

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
        var expanded = expandReferences && (previousProbe is null || !ReferencesEqual(probe, previousProbe))
            ? await references.GetAllReferencesAsync(token) : null;
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            token.ThrowIfCancellationRequested();
            if (expanded is not null) { _expandedReferenceProbe = probe; _expandedReferences = expanded; }
            var metadata = _expandedReferenceProbe is not null && ReferencesEqual(probe, _expandedReferenceProbe) ? _expandedReferences! : probe;
            var activeId = snapshot.Projection is { } projection
                ? snapshot.Documents.First(file => file.Path == projection.ToProject(position).Path).Id : snapshot.ActiveDocumentId;
            try {
                var document = await UpdateProjectAsync(snapshot, metadata, activeId, token);
                return await query(document, snapshot, token);
            }
            catch { _generationPending = true; throw; }
        }, token);
    }

    private static bool ReferencesEqual(IEnumerable<MetadataReference> first, IEnumerable<MetadataReference> second)
        => first.SequenceEqual(second) || first.ToHashSet().SetEquals(second);

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
        if (!ReferencesEqual(_project.MetadataReferences, references)) _project = _project.WithMetadataReferences(references);
        var files = snapshot.Documents.Append(new CSharpSourceDocument("$global-usings", "GlobalUsings.g.cs",
            "GlobalUsings.g.cs", snapshot.GlobalUsings, false)).ToArray();
        await SynchronizeDocumentsAsync(files, generated: false, cancellationToken);
        _generationPending |= generatorChanged || _project != previous;
        if (_generationPending) {
            await RunGeneratorsAsync(cancellationToken);
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

    private async Task RunGeneratorsAsync(CancellationToken cancellation)
    {
        var input = _project!.RemoveDocuments([.. _documents.Values.Where(static entry => entry.Generated).Select(static entry => entry.Id)]);
        var compilation = await input.GetCompilationAsync(cancellation)
            ?? throw new InvalidOperationException("The C# project could not be compiled.");
        _generatorDriver = _generatorDriver!.RunGenerators(compilation, cancellation);
        var generated = _generatorDriver.GetRunResult();
        _generatorDiagnostics = generated.Diagnostics;
        await SynchronizeDocumentsAsync(generated.GeneratedTrees.Select(tree => new CSharpSourceDocument(
            "$generated:" + tree.FilePath, tree.FilePath, tree.FilePath, tree.GetText(cancellation).ToString(), false)),
            generated: true, cancellation);
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
                var text = ApplySourceChange(existing.Text, file.Source);
                _project = _project!.GetDocument(existing.Id)!.WithText(text).WithFilePath(file.Path).Project;
                _documents[file.Id] = (existing.Id, text, generated);
            }
            else if (_project!.GetDocument(existing.Id)!.FilePath != file.Path) {
                _project = _project.GetDocument(existing.Id)!.WithFilePath(file.Path).Project;
            }
        }
    }

    private static SourceText ApplySourceChange(SourceText original, string source)
    {
        var before = original.ToString();
        var start = 0;
        while (start < before.Length && start < source.Length && before[start] == source[start]) start++;
        var end = before.Length;
        var newEnd = source.Length;
        while (end > start && newEnd > start && before[end - 1] == source[newEnd - 1]) {
            end--;
            newEnd--;
        }
        return original.WithChanges(new TextChange(TextSpan.FromBounds(start, end), source[start..newEnd]));
    }
}
