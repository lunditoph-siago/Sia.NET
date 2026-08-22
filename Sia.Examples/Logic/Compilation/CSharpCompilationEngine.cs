using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Notebook;

internal sealed class CSharpCompilationEngine(ICompilationReferenceResolver references)
{
    private readonly ICompilationReferenceResolver _references = references;

    public async Task<CSharpCompilationOutput> CompileAsync(
        CSharpCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trees = request.Sources.Select(source => new SourceTree(
            source,
            CSharpSyntaxTree.ParseText(
                SourceText.From(source.Source),
                CSharpLanguageOptions.Parse,
                source.Path,
                cancellationToken))).ToArray();
        var referenceProbe = string.Join('\n', request.Sources.Select(static source => source.Source));
        var metadata = await _references.GetReferencesAsync(referenceProbe, cancellationToken);
        var result = Emit(request, trees, metadata, cancellationToken);
        if (!result.EmitResult.Success && HasMissingReferenceDiagnostics(result.EmitResult.Diagnostics)) {
            metadata = await _references.GetAllReferencesAsync(cancellationToken);
            result = Emit(request, trees, metadata, cancellationToken);
        }

        return result.EmitResult.Success
            ? new(true, result.Image, result.Diagnostics)
            : new(false, null, result.Diagnostics);
    }

    private static EmitOutput Emit(
        CSharpCompilationRequest request,
        IReadOnlyList<SourceTree> trees,
        IReadOnlyList<MetadataReference> references,
        CancellationToken cancellationToken)
    {
        var compilation = CSharpCompilation.Create(
            request.AssemblyName,
            [.. trees.Select(static source => source.Tree)],
            references,
            CSharpLanguageOptions.ConsoleApplication);
        var expanded = GeneratorPipeline.Run(
            compilation,
            CSharpLanguageOptions.Parse,
            out var generatorDiagnostics);

        using var stream = new MemoryStream();
        var emitResult = expanded.Emit(stream, cancellationToken: cancellationToken);
        var diagnostics = BuildDiagnostics(
            emitResult.Diagnostics.Concat(generatorDiagnostics),
            trees);
        return new(
            emitResult,
            diagnostics,
            emitResult.Success ? stream.ToArray() : []);
    }

    private static IReadOnlyList<CSharpCompilationDiagnostic> BuildDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        IReadOnlyList<SourceTree> trees)
    {
        var sourcesByTree = trees.ToDictionary(
            static item => item.Tree,
            static item => item.Source);
        var sourcesByPath = trees
            .Select(static item => item.Source)
            .Where(static source => source.IsUserCode)
            .ToDictionary(static source => source.Path, StringComparer.Ordinal);
        List<CSharpCompilationDiagnostic> results = [];
        foreach (var diagnostic in diagnostics) {
            if (diagnostic.Severity == DiagnosticSeverity.Hidden) {
                continue;
            }

            var span = diagnostic.Location.GetMappedLineSpan();
            CSharpSourceDocument? source = null;
            if (diagnostic.Location.SourceTree is { } tree
                && sourcesByTree.TryGetValue(tree, out var treeSource)
                && treeSource.IsUserCode) {
                source = treeSource;
            } else if (span.HasMappedPath) {
                sourcesByPath.TryGetValue(span.Path, out source);
            }

            results.Add(new(
                diagnostic.Id,
                diagnostic.GetMessage(),
                diagnostic.Severity switch {
                    DiagnosticSeverity.Error => NotebookDiagnosticSeverity.Error,
                    DiagnosticSeverity.Warning => NotebookDiagnosticSeverity.Warning,
                    _ => NotebookDiagnosticSeverity.Info,
                },
                source?.Id,
                source?.DisplayPath,
                source is null ? 0 : span.StartLinePosition.Line + 1,
                source is null ? 0 : span.StartLinePosition.Character + 1));
        }
        return results;
    }

    private static bool HasMissingReferenceDiagnostics(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Id is "CS0246" or "CS0234" or "CS0012");

    private sealed record SourceTree(CSharpSourceDocument Source, SyntaxTree Tree);

    private readonly record struct EmitOutput(
        EmitResult EmitResult,
        IReadOnlyList<CSharpCompilationDiagnostic> Diagnostics,
        byte[] Image);
}
