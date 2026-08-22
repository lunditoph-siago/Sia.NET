using System.Threading;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed class EditorProjectCompiler(ICompilationReferenceResolver references)
{
    private static int _programCounter;

    private readonly CSharpCompilationEngine _engine = new(references);

    public async Task<CompileResult> CompileAsync(
        IReadOnlyList<File> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var globalUsings = NotebookLanguageContext.GetGlobalUsings(includeWrapperNamespace: true);
        List<CSharpSourceDocument> sources = [
            new(
                "$global-usings",
                "Editor/GlobalUsings.g.cs",
                "GlobalUsings.g.cs",
                globalUsings,
                IsUserCode: false),
            new(
                "$render-support",
                "Editor/Render.g.cs",
                "Render.g.cs",
                NotebookProgramBuilder.BuildStandaloneRenderSupport(),
                IsUserCode: false),
        ];
        sources.AddRange(files.Select((file, index) => new CSharpSourceDocument(
            file.Id,
            $"Editor/File{index}.cs",
            file.Name,
            file.Source)));
        var result = await _engine.CompileAsync(
            new(
                $"EditorProject_{Interlocked.Increment(ref _programCounter)}",
                sources),
            cancellationToken);
        return new(
            result.Success,
            result.AssemblyImage,
            [.. result.Diagnostics.Select(static diagnostic => new DiagnosticInfo(
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Severity,
                diagnostic.SourceId,
                diagnostic.SourcePath,
                diagnostic.Line,
                diagnostic.Column))]);
    }

    public static async Task<RunResult> ExecuteAsync(byte[] assemblyImage)
    {
        var result = await ManagedAssemblyExecutor.ExecuteAsync(assemblyImage);
        var rendered = RenderOutputProtocol.Split(result.StdOut);
        return new(
            result.Success,
            rendered.StandardOutput,
            result.StdErr,
            rendered.RenderRequested,
            rendered.RenderOutput);
    }

    internal sealed record File(string Id, string Name, string Source);

    internal sealed record DiagnosticInfo(
        string Id,
        string Message,
        NotebookDiagnosticSeverity Severity,
        string? FileId,
        string? FileName,
        int Line,
        int Column);

    internal sealed record CompileResult(
        bool Success,
        byte[]? AssemblyImage,
        IReadOnlyList<DiagnosticInfo> Diagnostics);

    internal sealed record RunResult(
        bool Success,
        string StandardOutput,
        string StandardError,
        bool RenderRequested,
        string RenderOutput);
}
