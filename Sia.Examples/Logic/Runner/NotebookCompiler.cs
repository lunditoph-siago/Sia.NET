using System.Threading;

namespace Sia_Examples.Notebook;

public sealed class NotebookCompiler
{
    private static int _programCounter;

    private readonly string _assemblyName;
    private readonly CSharpCompilationEngine _engine;

    private NotebookProgram _program;

    public NotebookCompiler(
        NotebookProgram program,
        ICompilationReferenceResolver referenceResolver)
    {
        _engine = new(referenceResolver);
        _assemblyName = $"NotebookProgram_{Interlocked.Increment(ref _programCounter)}";
        _program = program;
    }

    public void UpdateProgram(NotebookProgram program) => _program = program;

    public async Task<NotebookCompileResult> CompileAsync(CancellationToken cancellationToken = default)
    {
        var globalUsings = NotebookLanguageContext.GetGlobalUsings(
            _program.NeedsWrapperUsing);
        var sources = new List<CSharpSourceDocument>(_program.CompilationSources.Count + 2) {
            new(
                "$global-usings",
                "Notebook/GlobalUsings.g.cs",
                "GlobalUsings.g.cs",
                globalUsings,
                IsUserCode: false),
            new(
                "$render-support",
                "Notebook/Render.g.cs",
                "Render.g.cs",
                NotebookProgramBuilder.BuildStandaloneRenderSupport(),
                IsUserCode: false),
        };
        sources.AddRange(_program.CompilationSources);
        var result = await _engine.CompileAsync(
            new(_assemblyName, sources),
            cancellationToken);
        var diagnostics = result.Diagnostics.Select(static diagnostic => new NotebookDiagnostic(
            diagnostic.Id,
            diagnostic.Message,
            diagnostic.Severity,
            diagnostic.Line,
            diagnostic.Column,
            diagnostic.SourceId is not null,
            diagnostic.SourceId)).ToArray();
        return new(result.Success, result.AssemblyImage, diagnostics);
    }

    public static Task<NotebookExecuteResult> ExecuteAsync(byte[] assemblyImage)
        => ManagedAssemblyExecutor.ExecuteAsync(assemblyImage);
}
