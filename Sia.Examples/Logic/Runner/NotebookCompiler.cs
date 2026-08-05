using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Notebook;

public enum NotebookDiagnosticSeverity { Info, Warning, Error }

public readonly record struct NotebookDiagnostic(
    string Id,
    string Message,
    NotebookDiagnosticSeverity Severity,
    int Line,
    int Column,
    bool InUserCode);

public sealed record NotebookCompileResult(
    bool Success,
    byte[]? AssemblyImage,
    IReadOnlyList<NotebookDiagnostic> Diagnostics);

public sealed record NotebookExecuteResult(bool Success, string StdOut, string StdErr);

public sealed class NotebookCompiler : IDisposable
{
    private static int _programCounter;

    private const string BaseGlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Sia;
        """;

    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private readonly DocumentId _documentId;
    private readonly DocumentId _globalUsingsId;
    private readonly IMetadataReferenceProvider _referenceProvider;
    private readonly CSharpParseOptions _parseOptions = CSharpParseOptions.Default;

    public NotebookCompiler(NotebookProgram program, IMetadataReferenceProvider referenceProvider)
    {
        _referenceProvider = referenceProvider;
        _workspace = new AdhocWorkspace();

        _projectId = ProjectId.CreateNewId();
        var assemblyName = $"NotebookProgram_{Interlocked.Increment(ref _programCounter)}";
        _documentId = DocumentId.CreateNewId(_projectId, "Cell.cs");
        _globalUsingsId = DocumentId.CreateNewId(_projectId, "GlobalUsings.g.cs");

        var compilationOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            .WithConcurrentBuild(false)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var solution = _workspace.CurrentSolution
            .AddProject(_projectId, assemblyName, assemblyName, LanguageNames.CSharp)
            .WithProjectCompilationOptions(_projectId, compilationOptions)
            .WithProjectParseOptions(_projectId, _parseOptions)
            .AddDocument(_documentId, "Cell.cs", SourceText.From(program.Source))
            .AddDocument(_globalUsingsId, "GlobalUsings.g.cs", SourceText.From(GlobalUsings(program.NeedsWrapperUsing)));

        _workspace.TryApplyChanges(solution);
    }

    public Document CurrentDocument => _workspace.CurrentSolution.GetDocument(_documentId)!;

    public void UpdateProgram(NotebookProgram program)
    {
        var solution = _workspace.CurrentSolution
            .WithDocumentText(_documentId, SourceText.From(program.Source))
            .WithDocumentText(_globalUsingsId, SourceText.From(GlobalUsings(program.NeedsWrapperUsing)));
        _workspace.TryApplyChanges(solution);
    }

    private static string GlobalUsings(bool needsWrapperUsing)
        => needsWrapperUsing
            ? $"{BaseGlobalUsings}\nglobal using {NotebookProgramBuilder.WrapperNamespace};"
            : BaseGlobalUsings;

    public async Task<NotebookCompileResult> CompileAsync(CancellationToken cancellationToken = default)
    {
        var document = CurrentDocument;
        var references = await _referenceProvider.GetReferencesAsync().ConfigureAwait(false);
        var solution = document.Project.Solution.WithProjectMetadataReferences(document.Project.Id, references);
        document = solution.GetDocument(document.Id)!;

        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false))!;
        var userTree = compilation.SyntaxTrees.First();

        var expanded = GeneratorPipeline.Run(compilation, _parseOptions, out var generatorDiagnostics);

        using var stream = new MemoryStream();
        var emitResult = expanded.Emit(stream, cancellationToken: cancellationToken);
        var diagnostics = BuildDiagnostics(emitResult.Diagnostics.Concat(generatorDiagnostics), userTree);

        if (!emitResult.Success) {
            return new(false, null, diagnostics);
        }

        return new(true, stream.ToArray(), diagnostics);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Loads a freshly emitted in-memory assembly, not application code trimming can affect.")]
    public static async Task<NotebookExecuteResult> ExecuteAsync(byte[] assemblyImage)
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(stdOut);
        Console.SetError(stdErr);
        try {
            // Assembly.Load must live inside the try: it can legitimately throw (a bad
            // image, an AOT/trimming edge case, ...), and previously that exception
            // propagated straight out of ExecuteAsync — past the caller's `_running = true`
            // in NotebookSession.RunThroughAsync — leaving the session permanently "busy"
            // with no way to recover short of discarding it.
            var assembly = Assembly.Load(assemblyImage);
            var entryPoint = assembly.EntryPoint
                ?? throw new InvalidOperationException("No entry point found in the compiled program.");
            var result = entryPoint.Invoke(null, [Array.Empty<string>()]);
            if (result is Task task) {
                await task.ConfigureAwait(false);
            }
            return new(true, stdOut.ToString(), stdErr.ToString());
        }
        catch (Exception e) {
            var inner = e is TargetInvocationException { InnerException: { } captured } ? captured : e;
            stdErr.WriteLine(inner.ToString());
            return new(false, stdOut.ToString(), stdErr.ToString());
        }
        finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private static IReadOnlyList<NotebookDiagnostic> BuildDiagnostics(
        IEnumerable<Diagnostic> diagnostics, SyntaxTree userTree)
    {
        List<NotebookDiagnostic> results = [];
        foreach (var diagnostic in diagnostics) {
            if (diagnostic.Severity == DiagnosticSeverity.Hidden) {
                continue;
            }
            var inUserCode = diagnostic.Location.SourceTree == userTree;
            var lineSpan = diagnostic.Location.GetLineSpan();
            results.Add(new(
                diagnostic.Id,
                diagnostic.GetMessage(),
                diagnostic.Severity switch {
                    DiagnosticSeverity.Error => NotebookDiagnosticSeverity.Error,
                    DiagnosticSeverity.Warning => NotebookDiagnosticSeverity.Warning,
                    _ => NotebookDiagnosticSeverity.Info,
                },
                inUserCode ? lineSpan.StartLinePosition.Line + 1 : 0,
                inUserCode ? lineSpan.StartLinePosition.Character + 1 : 0,
                inUserCode));
        }
        return results;
    }

    public void Dispose() => _workspace.Dispose();
}
