using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly EditorShaderCompiler _shaderCompiler;
    private DomElement _shaderEntry = null!;
    private DomElement _shaderView = null!;
    private DomElement _shaderOutput = null!;
    private DomElement _shaderStatus = null!;
    private DomElement _shaderScope = null!;
    private EditorShaderPreview? _shaderPreview;
    private string? _shaderPreviewSnapshot;
    private ShaderCompilation? _shaderCompilation;
    private ShaderArtifact? _shaderArtifact;
    private readonly Dictionary<int, ShaderArtifact> _shaderArtifacts = [];
    private CancellationTokenSource? _shaderCancellation;
    private ShaderRequest? _pendingShaderRequest;
    private string? _shaderFolder;
    private string? _shaderSourcesHash;
    private string? _selectedShaderName;
    private int _shaderRevision;

    private enum ShaderAction { Compile, PreviewWgsl, PreviewSpirv, PipelineWgsl, PipelineSpirv }
    private sealed record ShaderRequest(ShaderAction Action, string Folder, string? EntryName);

    private void InitShaderPanel()
    {
        var panel = Own(DomElement.Create("section").Class("editor-shader"));
        var actions = Own(DomElement.Create("div").Class("editor-shader-actions"));
        _shaderEntry = Own(DomElement.Create("select").Attr("aria-label", "Shader entry")
            .On("change", "editor-shader-entry"));
        _shaderView = Own(DomElement.Create("select").Attr("aria-label", "Shader artifact")
            .On("change", "editor-shader-view"));
        foreach (var (value, text) in new[] { ("wgsl", "WGSL"), ("spirv", "SPIR-V bytes"), ("translated", "SPIR-V → WGSL") }) {
            using var option = DomElement.Create("option").Attr("value", value).Text(text);
            _shaderView.Append(option);
        }
        actions.Append(_shaderEntry)
            .Append(CreateAction("Compile Shader", "editor-shader-compile", "Compile C# files in the active file's folder as a shader library"))
            .Append(CreateAction("Preview WGSL", "editor-shader-wgsl", "Render the selected fragment shader"))
            .Append(CreateAction("Preview SPIR-V", "editor-shader-spirv", "Render through managed SPIR-V to WGSL translation"))
            .Append(CreateAction("Run Pipeline", "editor-pipeline-wgsl", "Run the active folder's ReactiveComponent with WGSL shaders"))
            .Append(CreateAction("Pipeline SPIR-V", "editor-pipeline-spirv", "Run the ReactiveComponent through SPIR-V to WGSL"))
            .Append(CreateAction("Stop Pipeline", "editor-pipeline-stop", "Release the pipeline session and its ECS world"))
            .Append(CreateAction("Cancel", "editor-shader-cancel", "Cancel the job; an active synchronous phase may finish"));
        _shaderScope = Own(DomElement.Create("div").Class("editor-shader-status")
            .Text("Open a shader file, or create the Shader Example. Compilation includes C# files under its folder."));
        _shaderStatus = Own(DomElement.Create("div").Id("editor-shader-status")
            .Class("editor-shader-status").Attr("role", "status").Text("No GPU preview yet"));
        var preview = Own(DomElement.Create("div").Id("editor-shader-preview").Class("editor-shader-preview"));
        var pipeline = Own(DomElement.Create("div").Id("editor-pipeline-preview").Class("editor-shader-preview"));
        var artifacts = Own(DomElement.Create("details").Class("editor-shader-artifacts"));
        var summary = Own(DomElement.Create("summary").Text("Compiled shader"));
        var artifactActions = Own(DomElement.Create("div").Class("editor-shader-actions"));
        artifactActions.Append(_shaderView)
            .Append(CreateAction("Download .spv", "editor-shader-download", "Download the compiled SPIR-V binary"));
        _shaderOutput = Own(DomElement.Create("pre").Class("editor-shader-output").Text("No compiled shader"));
        artifacts.Append(summary).Append(artifactActions).Append(_shaderOutput);
        panel.Append(actions).Append(_shaderScope).Append(_shaderStatus).Append(preview).Append(pipeline).Append(artifacts);
        _renderPanel.Append(panel);
    }

    private bool RouteShader(string eventName)
    {
        switch (eventName) {
            case "editor-pipeline-example": Track(CreatePipelineExampleAsync()); return true;
            case "editor-pipeline-wgsl": StartShaderOperation(ShaderAction.PipelineWgsl); return true;
            case "editor-pipeline-spirv": StartShaderOperation(ShaderAction.PipelineSpirv); return true;
            case "editor-pipeline-stop":
                StopPipeline();
                _shaderStatus.Text("Pipeline stopped");
                _buildStatus.Text("Pipeline stopped");
                return true;
            case "editor-shader-example": Track(CreateShaderExampleAsync()); return true;
            case "editor-shader-compile": StartShaderOperation(ShaderAction.Compile); return true;
            case "editor-shader-wgsl": StartShaderOperation(ShaderAction.PreviewWgsl); return true;
            case "editor-shader-spirv": StartShaderOperation(ShaderAction.PreviewSpirv); return true;
            case "editor-shader-cancel":
                _pendingShaderRequest = null;
                CancelShaderOperation("Cancelled · an active compiler phase may still finish");
                return true;
            case "editor-shader-entry":
                if (_shaderCompilation is { } compilation && int.TryParse(_shaderEntry.Value(), out var token)) {
                    _selectedShaderName = compilation.Entries.Single(entry => entry.MetadataToken == token).SelectionKey;
                    StartShaderOperation(new ShaderRequest(ShaderAction.Compile, _shaderFolder!, _selectedShaderName));
                }
                return true;
            case "editor-shader-view": RefreshShaderArtifact(); return true;
            case "editor-shader-download":
                if (_shaderArtifact is { } artifact) EditorShaderPreview.Download(artifact);
                return true;
            default: return false;
        }
    }

    private async Task CreateShaderExampleAsync()
    {
        SaveEditorSources();
        if (!_workspace.TryGetEntity(ShaderExamples.Folder, out _)) {
            await _workspace.CreateFolderAsync("", ShaderExamples.Folder, _lifetime.Token);
        }
        var path = ShaderExamples.Folder + "/" + ShaderExamples.FileName;
        if (!_workspace.TryGetEntity(path, out var file)) {
            file = await _workspace.CreateFileAsync(ShaderExamples.Folder, ShaderExamples.FileName, _lifetime.Token);
            _workspace.SetContent(file, ShaderExamples.Source);
            await _workspace.SaveAsync(file, _lifetime.Token);
        }
        if (_disposed) return;
        RenderExplorer();
        await OpenEntryAsync(file, pin: true);
        OpenPanelWindow("render");
        UpdateChrome("Shader example ready · choose an entry and preview");
    }

    private void StartShaderOperation(ShaderAction action)
    {
        SaveEditorSources();
        if (_activeEntity is not { } active || !active.IsValid || active.Contains<UntitledFile>()) {
            _buildStatus.Text("Open a saved shader file first");
            return;
        }
        StartShaderOperation(new ShaderRequest(action,
            WorkspacePath.Parent(EditorWorkspace.PathOf(active)) ?? "", _selectedShaderName));
    }

    private void StartShaderOperation(ShaderRequest request)
    {
        if (_busy) {
            if (_shaderCancellation is not null) {
                _pendingShaderRequest = request;
                CancelShaderOperation("New shader job queued · finishing the current phase");
            }
            else {
                _buildStatus.Text("Wait for the workspace build to finish");
            }
            return;
        }
        SaveEditorSources();
        var folder = request.Folder;
        if (_shaderFolder != folder) {
            _shaderFolder = folder;
            _shaderSourcesHash = null;
            _shaderCompilation = null;
            _shaderArtifact = null;
            _shaderArtifacts.Clear();
        }
        _selectedShaderName = request.EntryName;
        CheckShaderSources();
        _busy = true;
        _shaderCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var revision = ++_shaderRevision;
        _shaderPreview?.Invalidate(revision);
        _diagnostics.Text(string.Empty);
        _consoleOutput.Text("Compiling shader library…").ToggleClass("output-error", false);
        _buildStatus.Text("Compiling shader library…").ToggleClass("busy", true);
        OpenPanelWindow("render");
        _shaderScope.Text($"Source: {(folder.Length == 0 ? "workspace root" : folder)}/*.cs · C# library");
        DomRuntime.Flush();
        Track(BuildShaderAsync(request.Action, revision, _shaderCancellation));
    }

    private EditorProjectCompiler.File[] ShaderFiles()
        => _workspace.AllFiles()
            .Where(entity => !entity.Contains<UntitledFile>()
                && EditorWorkspace.PathOf(entity).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && (_shaderFolder == "" || WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(entity), _shaderFolder!)))
            .OrderBy(EditorWorkspace.PathOf, StringComparer.Ordinal)
            .Select(entity => new EditorProjectCompiler.File(KeyFor(entity), EditorWorkspace.PathOf(entity), entity.Get<WorkspaceFile>().Content))
            .ToArray();

    private static string ShaderHash(IReadOnlyList<EditorProjectCompiler.File> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files) {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Name));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(file.Source));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void CheckShaderSources()
    {
        if (_shaderFolder is null || _shaderSourcesHash is null || _disposed) return;
        SaveEditorSources();
        var current = ShaderHash(ShaderFiles());
        if (current == _shaderSourcesHash && (_shaderCompilation is null
            || _shaderCompilation.ReferenceVersion == _shaderCompiler.ReferenceVersion)) return;
        _shaderSourcesHash = current;
        _shaderCompilation = null;
        _shaderArtifact = null;
        _shaderArtifacts.Clear();
        _shaderEntry.Text(string.Empty);
        _shaderOutput.Text("Source changed · compile to update the artifacts");
        CancelShaderOperation("Shader source or references changed · compile to update");
    }

    private void CancelShaderOperation(string message)
    {
        ++_shaderRevision;
        _shaderCancellation?.Cancel();
        _shaderPreview?.Invalidate(_shaderRevision);
        _shaderStatus.Text(message + PreviousShaderPreview());
    }

    private string PreviousShaderPreview() => _shaderPreviewSnapshot is { } snapshot
        ? $" · showing previous preview {snapshot}" : string.Empty;

    private bool CurrentShaderJob(int revision)
    {
        if (_disposed || revision != _shaderRevision) return false;
        CheckShaderSources();
        return revision == _shaderRevision;
    }

    private async Task BuildShaderAsync(ShaderAction action, int revision, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try {
            var pipelineAction = action is ShaderAction.PipelineWgsl or ShaderAction.PipelineSpirv;
            await _workspace.EnsureAllLoadedAsync(token);
            if (!CurrentShaderJob(revision)) return;
            var files = ShaderFiles();
            var hash = ShaderHash(files);
            var compilationCached = _shaderCompilation is { Success: true } && hash == _shaderSourcesHash
                && (pipelineAction || _shaderCompilation.Entries.Count > 0);
            if (!compilationCached) {
                _shaderSourcesHash = hash;
                _shaderArtifacts.Clear();
                var compilation = await _shaderCompiler.CompileAsync(files, token, requireEntry: !pipelineAction);
                if (!CurrentShaderJob(revision)) return;
                if (compilation.ReferenceVersion != _shaderCompiler.ReferenceVersion) {
                    CancelShaderOperation("References changed · compile again to use the current references");
                    return;
                }
                _shaderCompilation = compilation;
                RenderDiagnostics(compilation.Diagnostics);
                if (!compilation.Success) {
                    _shaderStatus.Text("C# / IL analysis failed · see Console diagnostics" + PreviousShaderPreview());
                    _buildStatus.Text("Shader build failed");
                    OpenPanelWindow("console");
                    return;
                }
                _shaderEntry.Text(string.Empty);
                var selected = compilation.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName)
                    ?? compilation.Entries.FirstOrDefault();
                foreach (var entry in compilation.Entries) {
                    using var option = DomElement.Create("option")
                        .Attr("value", entry.MetadataToken.ToString(CultureInfo.InvariantCulture))
                        .Text($"{entry.DisplayName} · {entry.Stage}");
                    if (entry == selected) option.Attr("selected", "");
                    _shaderEntry.Append(option);
                }
                _selectedShaderName = selected?.SelectionKey;
            }
            var source = _shaderCompilation!;
            if (!source.Success) return;
            if (pipelineAction && source.Entries.Count == 0) {
                _buildStatus.Text("Creating programmable pipeline…");
                DomRuntime.Flush();
                await RunPipelineAsync(source, action == ShaderAction.PipelineSpirv, revision, token);
                return;
            }
            var kernel = source.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName) ?? source.Entries[0];
            _selectedShaderName = kernel.SelectionKey;
            _buildStatus.Text("Writing WGSL / SPIR-V…");
            DomRuntime.Flush();
            var artifactCached = _shaderArtifacts.TryGetValue(kernel.MetadataToken, out var artifact);
            if (artifact is null) {
                artifact = await Task.Run(() => EditorShaderCompiler.Write(source, kernel.MetadataToken, token), token);
                if (!CurrentShaderJob(revision)) return;
                if (_shaderArtifacts.Count >= 8) _shaderArtifacts.Remove(_shaderArtifacts.Keys.First());
                _shaderArtifacts.Add(kernel.MetadataToken, artifact);
            }
            if (!CurrentShaderJob(revision)) return;
            _shaderArtifact = artifact;
            RefreshShaderArtifact();
            _shaderScope.Text($"{_shaderFolder} · {files.Length} files · snapshot {hash[..8]} · {artifact.QualifiedName}");
            _consoleOutput.Text(FormattableString.Invariant(
                $"Compiled snapshot timings (ms) · source {(compilationCached ? "cached" : "new")} · artifact {(artifactCached ? "cached" : "new")}\nC# + references: {source.CSharpMilliseconds:F1}\nIL analysis: {source.AnalysisMilliseconds:F1}\nLowering: {artifact.LoweringMilliseconds:F1}\nWGSL: {artifact.WgslMilliseconds:F1}\nSPIR-V: {artifact.SpirvMilliseconds:F1}\nSPIR-V → WGSL: {artifact.TranslationMilliseconds:F1}\nWGSL: {artifact.Wgsl.Length} chars · SPIR-V: {artifact.Spirv.Length} bytes\n"));
            if (action == ShaderAction.Compile) {
                _shaderStatus.Text((artifact.Preview.UnsupportedReason ?? "Shader compiled · choose a preview route") + PreviousShaderPreview());
                _buildStatus.Text("Shader build succeeded");
                return;
            }
            if (action is ShaderAction.PipelineWgsl or ShaderAction.PipelineSpirv) {
                await RunPipelineAsync(source, action == ShaderAction.PipelineSpirv, revision, token);
                return;
            }
            _buildStatus.Text("Creating GPU pipeline…");
            DomRuntime.Flush();
            _shaderPreview ??= new("editor-shader-preview", "editor-shader-status");
            _shaderPreview.Invalidate(revision);
            var gpuMilliseconds = await _shaderPreview.ShowAsync(artifact, action == ShaderAction.PreviewSpirv, revision);
            if (!CurrentShaderJob(revision) || gpuMilliseconds < 0) return;
            StopPipeline(cancelPending: false);
            _shaderPreviewSnapshot = $"{hash[..8]} · {artifact.QualifiedName}";
            _consoleOutput.Text(_consoleOutput.TextContent() + FormattableString.Invariant($"GPU pipeline: {gpuMilliseconds:F1} ms\n"));
            _buildStatus.Text("Shader preview ready");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) {
            if (!_disposed) _buildStatus.Text("Shader job cancelled");
        }
        catch (Exception error) {
            if (CurrentShaderJob(revision)) {
                _shaderStatus.Text($"Shader failed: {error.Message}" + PreviousShaderPreview());
                _buildStatus.Text("Shader failed");
                _consoleOutput.Text(error.ToString()).ToggleClass("output-error", true);
            }
        }
        finally {
            if (!_disposed && token.IsCancellationRequested) _buildStatus.Text("Shader job cancelled");
            cancellation.Dispose();
            _shaderCancellation = null;
            _busy = false;
            if (!_disposed) {
                _buildStatus.ToggleClass("busy", false);
                DomRuntime.Flush();
                if (_pendingShaderRequest is { } next) {
                    _pendingShaderRequest = null;
                    StartShaderOperation(next);
                }
            }
        }
    }

    private void RefreshShaderArtifact()
    {
        if (_shaderArtifact is not { } artifact) return;
        _shaderOutput.Text(_shaderView.Value() switch {
            "spirv" => $"SPIR-V · {artifact.Spirv.Length} bytes · download for the full binary\n" +
                Convert.ToHexString(artifact.Spirv.AsSpan(0, Math.Min(artifact.Spirv.Length, 4096))),
            "translated" => artifact.TranslatedWgsl,
            _ => artifact.Wgsl,
        });
    }

    private void DisposeShaderPreview()
    {
        StopPipeline();
        _shaderPreviewSnapshot = null;
        CancelShaderOperation("Preview closed");
        _pendingShaderRequest = null;
        _shaderPreview?.Dispose();
        _shaderPreview = null;
    }
}
