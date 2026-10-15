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
    private DomElement _shaderTarget = null!;
    private DomElement _pipelineCanvas = null!;
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
    private EditorUniformInspector? _uniformInspector;

    private enum ShaderAction { Compile, Run }
    private sealed record ShaderRequest(ShaderAction Action, string Folder, string? EntryName, bool SpirvRoute, bool NormalCanvas);

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
        _shaderTarget = Own(DomElement.Create("select").Attr("aria-label", "Compiler target"));
        foreach (var (value, text) in new[] { ("wgsl", "WGSL"), ("spirv", "SPIR-V → WGSL") }) {
            using var option = DomElement.Create("option").Attr("value", value).Text(text);
            _shaderTarget.Append(option);
        }
        _pipelineCanvas = Own(DomElement.Create("select").Attr("aria-label", "Pipeline canvas")
            .Attr("title", "Canvas for C# pipelines. Normal canvas reads GPU pixels back; Offscreen presents directly."));
        foreach (var (value, text) in new[] { ("offscreen", "Offscreen canvas"), ("normal", "Normal canvas · readback") }) {
            using var option = DomElement.Create("option").Attr("value", value).Text(text);
            _pipelineCanvas.Append(option);
        }
        actions.Append(_shaderEntry).Append(_shaderTarget).Append(_pipelineCanvas)
            .Append(Own(DomElement.Create("span").Id("editor-uniform-inspector")))
            .Append(CreateAction("Stop", "editor-shader-stop", "Release the current rendering resources"))
            .Append(CreateAction("Cancel", "editor-shader-cancel", "Cancel the current compiler job"));
        _shaderScope = Own(DomElement.Create("div").Class("editor-shader-status")
            .Text("Compilation includes C# files under the active file’s folder."));
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
            case "editor-shader-compile": StartShaderOperation(ShaderAction.Compile); return true;
            case "editor-shader-run": StartShaderOperation(ShaderAction.Run); return true;
            case "editor-shader-stop":
                if (_runningMain) { _buildStatus.Text("Main is still running · arbitrary C# execution cannot be interrupted"); return true; }
                StopPipeline();
                _shaderPreview?.Dispose();
                _shaderPreview = null;
                _shaderPreviewSnapshot = null;
                _shaderStatus.Text("Rendering stopped");
                _buildStatus.Text("Rendering stopped");
                return true;
            case "editor-shader-cancel":
                if (_runningMain) { _buildStatus.Text("Main is still running · arbitrary C# execution cannot be interrupted"); return true; }
                _pendingShaderRequest = null;
                CancelShaderOperation("Cancelled · an active compiler phase may still finish");
                return true;
            case "editor-shader-entry":
                if (_shaderCompilation is { } compilation && int.TryParse(_shaderEntry.Value(), out var token)) {
                    _selectedShaderName = compilation.Entries.Single(entry => entry.MetadataToken == token).SelectionKey;
                    StartShaderOperation(new ShaderRequest(ShaderAction.Compile, _shaderFolder!, _selectedShaderName,
                        _shaderTarget.Value() == "spirv", _pipelineCanvas.Value() == "normal"));
                }
                return true;
            case "editor-shader-view": RefreshShaderArtifact(); return true;
            case "editor-shader-download":
                if (_shaderArtifact is { } artifact) EditorShaderPreview.Download(artifact);
                return true;
            default: return false;
        }
    }

    private async Task<ShaderArtifact> GetShaderArtifactAsync(ShaderCompilation source, int token,
        int revision, CancellationToken cancellationToken)
    {
        if (_shaderArtifacts.TryGetValue(token, out var artifact)) return artifact;
        artifact = await Task.Run(() => EditorShaderCompiler.Write(source, token, cancellationToken), cancellationToken);
        if (CurrentShaderJob(revision)) {
            if (_shaderArtifacts.Count >= 8) _shaderArtifacts.Remove(_shaderArtifacts.Keys.First());
            _shaderArtifacts.Add(token, artifact);
        }
        return artifact;
    }

    private void StartShaderOperation(ShaderAction action)
    {
        SaveEditorSources();
        if (_activeEntity is not { } active || !active.IsValid || active.Contains<UntitledFile>()) {
            _buildStatus.Text("Open a saved C# file first");
            return;
        }
        StartShaderOperation(new ShaderRequest(action,
            WorkspacePath.Parent(EditorWorkspace.PathOf(active)) ?? "", _selectedShaderName,
            _shaderTarget.Value() == "spirv", _pipelineCanvas.Value() == "normal"));
    }

    private void StartShaderOperation(ShaderRequest request)
    {
        if (_runningMain) {
            _buildStatus.Text("Main is still running · wait for it to finish");
            return;
        }
        if (_busy) {
            if (_shaderCancellation is not null) {
                _pendingShaderRequest = request;
                CancelShaderOperation("New shader job queued · finishing the current phase");
            }
            else {
                _buildStatus.Text("Wait for the current operation to finish");
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
        _consoleOutput.Text("Compiling C#…").ToggleClass("output-error", false);
        _buildStatus.Text("Compiling C#…").ToggleClass("busy", true);
        if (request.Action == ShaderAction.Compile) OpenPanelWindow("render");
        _shaderScope.Text($"Source: {(folder.Length == 0 ? "workspace root" : folder)}/*.cs · C# program");
        DomRuntime.Flush();
        Track(BuildShaderAsync(request, revision, _shaderCancellation));
    }

    private CSharpSourceDocument[] ShaderFiles()
        => _workspace.AllFiles()
            .Where(entity => !entity.Contains<UntitledFile>()
                && EditorWorkspace.PathOf(entity).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && (_shaderFolder == "" || WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(entity), _shaderFolder!)))
            .OrderBy(EditorWorkspace.PathOf, StringComparer.Ordinal)
            .Select(entity => new CSharpSourceDocument(KeyFor(entity), EditorWorkspace.PathOf(entity), EditorWorkspace.PathOf(entity), entity.Get<WorkspaceFile>().Content))
            .ToArray();

    private static string ShaderHash(IReadOnlyList<CSharpSourceDocument> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files) {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path));
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
        if (_runningMain && !_disposed) {
            _shaderStatus.Text("Main continues using its compiled snapshot · source changes apply to the next Run");
            return;
        }
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

    private async Task BuildShaderAsync(ShaderRequest request, int revision, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try {
            await _workspace.EnsureAllLoadedAsync(token);
            if (!CurrentShaderJob(revision)) return;
            var files = ShaderFiles();
            var hash = ShaderHash(files);
            var compilationCached = _shaderCompilation is { Success: true } && hash == _shaderSourcesHash;
            if (!compilationCached) {
                _shaderSourcesHash = hash;
                _shaderArtifacts.Clear();
                var compilation = await _shaderCompiler.CompileAsync(files, token, requireEntry: false);
                if (!CurrentShaderJob(revision)) return;
                if (compilation.ReferenceVersion != _shaderCompiler.ReferenceVersion) {
                    CancelShaderOperation("References changed · compile again to use the current references");
                    return;
                }
                _shaderCompilation = compilation;
                RenderDiagnostics(compilation.Diagnostics);
                if (!compilation.Success) {
                    _shaderStatus.Text("C# / IL analysis failed · see Diagnostics" + PreviousShaderPreview());
                    _buildStatus.Text("Shader build failed");
                    OpenPanelWindow("console");
                    return;
                }
                _shaderEntry.Text(string.Empty);
                var selected = compilation.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName)
                    ?? compilation.Entries.FirstOrDefault(entry => entry.Stage.ToString() == "Fragment")
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
            _shaderScope.Text($"{_shaderFolder} · {files.Length} files · snapshot {hash[..8]}");
            _consoleOutput.Text(FormattableString.Invariant(
                $"Compiled C# snapshot {hash[..8]} ({(compilationCached ? "cached" : "new")})\nC# + references: {source.CSharpMilliseconds:F1} ms\nIL analysis: {source.AnalysisMilliseconds:F1} ms\n"));
            if (request.Action == ShaderAction.Run) {
                if (await TryRunPipelineAsync(source, request.SpirvRoute, request.NormalCanvas, revision, token)) return;
                if (await TryRunMainAsync(source, revision, token)) return;
            }
            if (source.Entries.Count == 0) {
                _shaderArtifact = null;
                _shaderOutput.Text("Compiled C# · no shader entries");
                if (request.Action == ShaderAction.Run)
                    throw new InvalidOperationException("No runnable entry. Declare Render(in EditorPipelineFrame, ref Hooks), a standard Main/top-level program, or a fragment shader.");
                _shaderStatus.Text("C# compiled · no shader entries");
                _buildStatus.Text("Compilation succeeded");
                return;
            }
            var kernel = source.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName) ?? source.Entries[0];
            _selectedShaderName = kernel.SelectionKey;
            _buildStatus.Text("Writing WGSL / SPIR-V…");
            DomRuntime.Flush();
            var artifactCached = _shaderArtifacts.TryGetValue(kernel.MetadataToken, out var artifact);
            if (artifact is null) {
                artifact = await GetShaderArtifactAsync(source, kernel.MetadataToken, revision, token);
                if (!CurrentShaderJob(revision)) return;
            }
            if (!CurrentShaderJob(revision)) return;
            _shaderArtifact = artifact;
            RefreshShaderArtifact();
            _shaderScope.Text($"{_shaderFolder} · {files.Length} files · snapshot {hash[..8]} · {artifact.QualifiedName}");
            _consoleOutput.Text(FormattableString.Invariant(
                $"Compiled snapshot timings (ms) · source {(compilationCached ? "cached" : "new")} · artifact {(artifactCached ? "cached" : "new")}\nC# + references: {source.CSharpMilliseconds:F1}\nIL analysis: {source.AnalysisMilliseconds:F1}\nLowering: {artifact.LoweringMilliseconds:F1}\nWGSL: {artifact.WgslMilliseconds:F1}\nSPIR-V: {artifact.SpirvMilliseconds:F1}\nSPIR-V → WGSL: {artifact.TranslationMilliseconds:F1}\nWGSL: {artifact.Wgsl.Length} chars · SPIR-V: {artifact.Spirv.Length} bytes\n"));
            if (request.Action == ShaderAction.Compile) {
                _shaderStatus.Text((artifact.Preview.UnsupportedReason ?? "Shader compiled · select a target and Run") + PreviousShaderPreview());
                _buildStatus.Text("Shader build succeeded");
                return;
            }
            _buildStatus.Text("Creating GPU pipeline…");
            OpenPanelWindow("render");
            DomRuntime.Flush();
            _shaderPreview ??= new("editor-shader-preview", "editor-shader-status", _uniformInspector!);
            _shaderPreview.Invalidate(revision);
            var gpuMilliseconds = await _shaderPreview.ShowAsync(artifact, request.SpirvRoute, revision);
            if (!CurrentShaderJob(revision) || gpuMilliseconds < 0) return;
            StopPipeline(cancelPending: false);
            _renderOutput.Text("Fragment preview · compiler outputs rendered with WebGPU");
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
