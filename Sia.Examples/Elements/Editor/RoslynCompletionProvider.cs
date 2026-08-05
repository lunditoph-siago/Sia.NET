#if !BROWSER
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class RoslynCompletionProvider(IMetadataReferenceProvider referenceProvider) : IEditorCompletionProvider
{
    private static readonly Lazy<HostServices> Host = new(() =>
        Microsoft.CodeAnalysis.Host.Mef.MefHostServices.Create(
            Microsoft.CodeAnalysis.Host.Mef.MefHostServices.DefaultAssemblies
                .Append(typeof(CompletionService).Assembly)
                .Append(System.Reflection.Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"))));

    public async Task<CompletionQueryResult> QueryAsync(
        string source, int position, CancellationToken cancellationToken = default)
    {
        using var workspace = new AdhocWorkspace(Host.Value);
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId, "Completion.cs");

        var references = await referenceProvider.GetReferencesAsync().ConfigureAwait(false);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Completion", "Completion", LanguageNames.CSharp)
            .WithProjectMetadataReferences(projectId, references)
            .AddDocument(documentId, "Completion.cs", SourceText.From(source));
        workspace.TryApplyChanges(solution);

        var document = workspace.CurrentSolution.GetDocument(documentId)!;
        var service = CompletionService.GetService(document);
        if (service is null) {
            return CompletionQueryResult.Empty;
        }

        var list = await service.GetCompletionsAsync(document, position, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (list is null || list.ItemsList.Count == 0) {
            return CompletionQueryResult.Empty;
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
            items.Add(new CompletionCandidate(
                item.DisplayText, change.TextChange.NewText ?? "",
                change.TextChange.Span.Start, change.TextChange.Span.End));
        }

        return new CompletionQueryResult(items);
    }
}
#endif
