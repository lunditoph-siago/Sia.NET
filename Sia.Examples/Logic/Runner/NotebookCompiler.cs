using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Notebook;

public sealed class NotebookCompiler
{
    private static int _programCounter;

    private const string _baseGlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Sia;
        """;

    private readonly string _assemblyName;
    private readonly ICompilationReferenceResolver _referenceResolver;
    private readonly CSharpParseOptions _parseOptions = CSharpParseOptions.Default;
    private readonly CSharpCompilationOptions _compilationOptions =
        new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            .WithConcurrentBuild(false)
            .WithNullableContextOptions(NullableContextOptions.Enable);

    private NotebookProgram _program;

    public NotebookCompiler(
        NotebookProgram program,
        ICompilationReferenceResolver referenceResolver)
    {
        _referenceResolver = referenceResolver;
        _assemblyName = $"NotebookProgram_{Interlocked.Increment(ref _programCounter)}";
        _program = program;
    }

    public void UpdateProgram(NotebookProgram program) => _program = program;

    private static string GlobalUsings(bool needsWrapperUsing)
        => needsWrapperUsing
            ? $"{_baseGlobalUsings}\nglobal using {NotebookProgramBuilder.WrapperNamespace};"
            : _baseGlobalUsings;

    public async Task<NotebookCompileResult> CompileAsync(CancellationToken cancellationToken = default)
    {
        var globalUsings = GlobalUsings(_program.NeedsWrapperUsing);
        var references = await _referenceResolver.GetReferencesAsync(
            $"{globalUsings}\n{_program.Source}",
            cancellationToken);
        var (emitResult, diagnostics, image) = Emit(
            globalUsings,
            references,
            cancellationToken);

        if (!emitResult.Success && HasMissingReferenceDiagnostics(emitResult.Diagnostics)) {
            var allReferences = await _referenceResolver.GetAllReferencesAsync(cancellationToken);
            (emitResult, diagnostics, image) = Emit(
                globalUsings,
                allReferences,
                cancellationToken);
        }

        return emitResult.Success ? new(true, image, diagnostics) : new(false, null, diagnostics);
    }

    private (EmitResult EmitResult, IReadOnlyList<NotebookDiagnostic> Diagnostics, byte[] Image) Emit(
        string globalUsings,
        IReadOnlyList<MetadataReference> references,
        CancellationToken cancellationToken)
    {
        var userTree = CSharpSyntaxTree.ParseText(
            SourceText.From(_program.Source),
            _parseOptions,
            "Cell.cs",
            cancellationToken);
        var globalUsingsTree = CSharpSyntaxTree.ParseText(
            SourceText.From(globalUsings),
            _parseOptions,
            "GlobalUsings.g.cs",
            cancellationToken);
        var compilation = CSharpCompilation.Create(
            _assemblyName,
            [userTree, globalUsingsTree],
            references,
            _compilationOptions);

        var expanded = GeneratorPipeline.Run(compilation, _parseOptions, out var generatorDiagnostics);

        using var stream = new MemoryStream();
        var emitResult = expanded.Emit(stream, cancellationToken: cancellationToken);
        var diagnostics = BuildDiagnostics(emitResult.Diagnostics.Concat(generatorDiagnostics), userTree);

        return (emitResult, diagnostics, emitResult.Success ? stream.ToArray() : []);
    }

    private static bool HasMissingReferenceDiagnostics(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Id is "CS0246" or "CS0234" or "CS0012");

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
            var assembly = Assembly.Load(assemblyImage);
            var entryPoint = assembly.EntryPoint
                ?? throw new InvalidOperationException("No entry point found in the compiled program.");
            var result = entryPoint.Invoke(null, [Array.Empty<string>()]);
            if (result is Task task) {
                await task;
            }
            return new(true, stdOut.ToString(), stdErr.ToString());
        }
        catch (Exception e) {
            var inner = e is TargetInvocationException { InnerException: { } captured } ? captured : e;
            stdErr.WriteLine(inner.ToString());
            return new(false, stdOut.ToString(), stdErr.ToString());
        } finally {
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

}
