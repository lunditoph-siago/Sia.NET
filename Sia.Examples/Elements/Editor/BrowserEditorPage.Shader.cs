using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly EditorShaderCompiler _shaderCompiler;
    private DomElement _shaderEntry = null!;
    private DomElement _shaderView = null!;
    private DomElement _shaderOutput = null!;
    private DomElement _renderTools = null!;
    private DomElement _shaderScope = null!;
    private DomElement _shaderArtifactActions = null!;
    private EditorShaderPreview? _shaderPreview;
    private string? _shaderPreviewSnapshot;
    private ShaderCompilation? _shaderCompilation;
    private ShaderArtifact? _shaderArtifact;
    private readonly Dictionary<int, ShaderArtifact> _shaderArtifacts = [];
    private readonly EditorShaderJobs _shaderJobs = new();
    private string? _shaderFolder;
    private string? _shaderSourcesHash;
    private string? _selectedShaderName;
    private EditorUniformInspector? _uniformInspector;

    private void InitShaderPanel()
    {
        var panel = Own(DomElement.Create("section").Id("editor-render-surface").Class("editor-shader"));
        _renderTools = Own(DomElement.Create("div").Class("editor-render-tools")
            .Attr("role", "toolbar").Attr("aria-label", "Render controls"));
        _renderTools.Append(Own(DomElement.Create("span").Id("editor-uniform-inspector")
            .Attr("data-render-surface", "editor-render-surface")))
            .Append(Own(DomElement.Create("span").Id("editor-preview-actions")));
        _shaderEntry = Own(DomElement.Create("select").Attr("aria-label", "Shader entry")
            .On("change", "editor-shader-entry"));
        _shaderView = Own(DomElement.Create("select").Attr("aria-label", "Shader artifact")
            .On("change", "editor-shader-view"));
        foreach (var (value, text) in new[] { ("wgsl", "WGSL"), ("spirv", "SPIR-V bytes"), ("translated", "SPIR-V → WGSL") }) {
            using var option = DomElement.Create("option").Attr("value", value).Text(text);
            _shaderView.Append(option);
        }
        _shaderScope = Own(DomElement.Create("div").Class("editor-shader-status")
            .Text("Compilation includes C# files under the active file’s folder."));
        var preview = Own(DomElement.Create("div").Id("editor-shader-preview").Class("editor-shader-preview"));
        var pipeline = Own(DomElement.Create("div").Id("editor-pipeline-preview").Class("editor-shader-preview"));
        var stage = Own(DomElement.Create("div").Class("editor-shader-stage"));
        var surfaces = Own(DomElement.Create("div").Class("editor-shader-surfaces"));
        surfaces.Append(preview).Append(pipeline);
        stage.Append(surfaces).Append(Own(DomElement.Create("div").Id("editor-uniform-space")
            .Class("editor-uniform-space").Attr("aria-hidden", "true")));
        _shaderArtifactActions = Own(DomElement.Create("div").Class("editor-shader-actions").Class("hidden"));
        var entryLabel = Own(DomElement.Create("label").Text("Shader entry ")).Append(_shaderEntry);
        var artifactLabel = Own(DomElement.Create("label").Text("Output ")).Append(_shaderView);
        _shaderArtifactActions.Append(entryLabel).Append(artifactLabel)
            .Append(CreateAction("Download .spv", "editor-shader-download", "Download the compiled SPIR-V binary"));
        _shaderOutput = Own(DomElement.Create("pre").Class("editor-shader-output")
            .Text("Compile checks C# and produces WGSL / SPIR-V when shader entries exist.\nRun executes the C# RenderGraph, Main, or a compatible fragment preview."));
        _compilationDetailsToggle = CreateAction("Build details", "editor-compilation-details", "Show compiler timings and operation details")
            .Class("editor-compilation-details-toggle").Attr("aria-expanded", "false")
            .Attr("aria-controls", "editor-compilation-details");
        _compilationDetails = Own(DomElement.Create("div").Id("editor-compilation-details")
            .Class("editor-compilation-details").Class("hidden")).Append(_compilationOutput);
        _compilationPanel.Append(_shaderScope).Append(_shaderArtifactActions).Append(_shaderOutput)
            .Append(_compilationDetailsToggle).Append(_compilationDetails);
        panel.Append(stage);
        _renderPanel.Append(panel);
    }

    private bool RouteShader(string eventName)
    {
        switch (eventName) {
            case "editor-run-action": {
                    var action = RunActionState();
                    if (!action.Disabled) return RouteShader(action.Command);
                    SetBuildStatus(action.Title);
                    return true;
                }
            case "editor-status-details": ShowCompilationDetails(true); OpenPanelWindow("compilation"); return true;
            case "editor-compilation-details": ShowCompilationDetails(!_showCompilationDetails); return true;
            case "editor-compile-action": {
                    var action = CompileActionState();
                    if (!action.Disabled) return RouteShader(action.Command);
                    SetBuildStatus(action.Title);
                    return true;
                }
            case "editor-shader-compile": StartShaderOperation(ShaderAction.Compile); return true;
            case "editor-shader-run": StartShaderOperation(ShaderAction.Run); return true;
            case "editor-shader-stop":
                if (_runningMain) { SetBuildStatus("Main is still running · arbitrary C# execution cannot be interrupted"); return true; }
                StopRendering();
                return true;
            case "editor-shader-cancel":
                if (_runningMain) { SetBuildStatus("Main is still running · arbitrary C# execution cannot be interrupted"); return true; }
                CancelShaderOperation("Cancelled · an active compiler phase may still finish", clearPending: true);
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

    private (string Label, string Command, string Title, bool Disabled) CompileActionState()
    {
        if (_runningMain) return ("Compile", "editor-shader-compile", "Main is running · wait for it to finish", true);
        if (_shaderJobs.IsRunning) return ("Cancel", "editor-shader-cancel",
            _shaderJobs.IsCancellationRequested ? "Cancellation requested · waiting for the current phase" : "Cancel the current compiler job", _shaderJobs.IsCancellationRequested);
        return ("Compile", "editor-shader-compile", "Compile C# and inspect shader artifacts without executing the program", false);
    }

    private (string Label, string Command, string Title, bool Disabled) RunActionState()
    {
        if (_runningMain) return ("Run", "editor-shader-run", "Main is running · arbitrary C# execution cannot be interrupted", true);
        var rendering = _shaderPreview is not null;
#if BROWSER
        rendering |= _pipeline is not null;
#endif
        return rendering
            ? ("Stop", "editor-shader-stop", _shaderJobs.IsRunning ? "Wait for the current operation, or cancel compilation first" : "Release the current rendering resources", _shaderJobs.IsRunning)
            : ("Run", "editor-shader-run", _shaderJobs.IsRunning ? "Wait for the current operation" : "Run the C# program or RenderGraph from the active folder and its subfolders", _shaderJobs.IsRunning);
    }

    private void UpdateActions()
    {
        if (_disposed) return;
        _lastActionStatus.ToggleClass("hidden", _shaderJobs.IsRunning || _runningMain);
        UpdateIconAction(_runAction, RunActionState());
        UpdateIconAction(_compileAction, CompileActionState());
    }

    private static void UpdateIconAction(DomElement button, (string Label, string Command, string Title, bool Disabled) action)
        => button.Attr("data-action", action.Label.ToLowerInvariant())
            .Attr("aria-label", action.Label).Attr("title", action.Title)
            .Attr("aria-disabled", action.Disabled ? "true" : "false");

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
        SaveEditorSources(synchronizeNativeInput: true);
        if (_activeEntity is not { } active || !active.IsValid || active.Contains<UntitledFile>()
            || !EditorWorkspace.PathOf(active).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) {
            SetBuildStatus("Open a saved C# file first");
            return;
        }
        StartShaderOperation(new ShaderRequest(action,
            WorkspacePath.Parent(EditorWorkspace.PathOf(active)) ?? "", _selectedShaderName));
    }

    private void StartShaderOperation(ShaderRequest request)
    {
        if (_runningMain) {
            SetBuildStatus("Main is still running · wait for it to finish");
            return;
        }
        if (_shaderJobs.IsRunning) {
            _shaderJobs.Queue(request);
            ShowShaderCancellation("New shader job queued · finishing the current phase");
            return;
        }
        SaveEditorSources(synchronizeNativeInput: true);
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
        CancelLanguageDiagnostics();
        var job = _shaderJobs.Start(request, _lifetime.Token);
        UpdateActions();
        var revision = job.Revision;
        _shaderPreview?.Invalidate(revision);
        ClearDiagnostics();
        ResetDiagnosticStatus();
        _operationError.Text(string.Empty);
        ShowCompilationDetails(false);
        _compilationOutput.Text("Compiling C#…").ToggleClass("output-error", false);
        SetBuildStatus("Compiling C#…").ToggleClass("busy", true);
        if (request.Action == ShaderAction.Compile) OpenPanelWindow("compilation");
        _shaderScope.Text($"Source: {SourceScope()} · including subfolders");
        DomRuntime.Flush();
        Track(BuildShaderAsync(job));
    }

    private string SourceScope() => string.IsNullOrEmpty(_shaderFolder) ? "workspace root" : _shaderFolder;

    private CSharpSourceDocument[] ShaderFiles()
        => _workspace.AllFiles()
            .Where(entity => !entity.Contains<UntitledFile>()
                && EditorWorkspace.PathOf(entity).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && (_shaderFolder == "" || WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(entity), _shaderFolder!)))
            .OrderBy(EditorWorkspace.PathOf, StringComparer.Ordinal)
            .Select(entity => new CSharpSourceDocument(KeyFor(entity), EditorWorkspace.PathOf(entity), EditorWorkspace.PathOf(entity), entity.Get<WorkspaceFile>().Content))
            .ToArray();

    private string ShaderHash(IReadOnlyList<CSharpSourceDocument> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(PackageSource()));
        hash.AppendData([0]);
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
        ClearDiagnostics();
        ResetDiagnosticStatus();
        _shaderArtifactActions.ToggleClass("hidden", true);
        _shaderOutput.Text("Source changed · compile to update the artifacts");
        _shaderScope.Text($"Source changed · {SourceScope()} · including subfolders · compile to update");
        CancelShaderOperation("Source changed · compile to update");
        if (!_shaderJobs.IsRunning && !_runningMain) SetBuildStatus("Source changed · compile to update" + PreviousShaderPreview());
    }

    private void CancelShaderOperation(string message, bool clearPending = false)
    {
        if (_runningMain && !_disposed) {
            SetBuildStatus("Main continues using its compiled snapshot · source changes apply to the next Run");
            return;
        }
        _shaderJobs.Cancel(clearPending);
        ShowShaderCancellation(message);
    }

    private void ShowShaderCancellation(string message)
    {
        _shaderPreview?.Invalidate(_shaderJobs.Revision);
        SetBuildStatus(message + PreviousShaderPreview());
        UpdateActions();
    }

    private string PreviousShaderPreview()
    {
        if (_shaderPreviewSnapshot is not { } snapshot) return string.Empty;
        var current = _shaderSourcesHash is { } hash && snapshot.StartsWith(hash[..8], StringComparison.Ordinal);
        return $" · {(current ? "rendering snapshot" : "showing previous snapshot")} {snapshot}";
    }

    private bool CurrentShaderJob(int revision)
    {
        if (_disposed || !_shaderJobs.IsCurrent(revision)) return false;
        CheckShaderSources();
        return _shaderJobs.IsCurrent(revision);
    }

    private async Task BuildShaderAsync(EditorShaderJob job)
    {
        var request = job.Request;
        var revision = job.Revision;
        var token = job.Token;
        var clock = Stopwatch.StartNew();
        try {
            await _workspace.EnsureAllLoadedAsync(token);
            await EnsureEditorPackagesAsync(token);
            if (!CurrentShaderJob(revision)) return;
            var files = ShaderFiles();
            var hash = ShaderHash(files);
            var compilationCached = _shaderCompilation is { Success: true } && hash == _shaderSourcesHash;
            if (!compilationCached) {
                _shaderSourcesHash = hash;
                _shaderArtifacts.Clear();
                var compilation = await _shaderCompiler.CompileAsync(files, token);
                if (!CurrentShaderJob(revision)) return;
                if (compilation.ReferenceVersion != _shaderCompiler.ReferenceVersion) {
                    CancelShaderOperation("References changed · compile again to use the current references");
                    return;
                }
                _shaderCompilation = compilation;
                RenderDiagnostics(compilation.Diagnostics);
                if (!compilation.Success) {
                    SetBuildStatus("C# compilation failed · see Compilation" + PreviousShaderPreview());
                    OpenPanelWindow("compilation");
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
                _shaderArtifactActions.ToggleClass("hidden", compilation.Entries.Count == 0);
            }
            var source = _shaderCompilation!;
            if (!source.Success) return;
            if (compilationCached) RenderDiagnostics(source.Diagnostics);
            _shaderScope.Text($"{SourceScope()} · {files.Length} C# files including subfolders · snapshot {hash[..8]}");
            _compilationOutput.Text(FormattableString.Invariant(
                $"Compiled C# snapshot {hash[..8]} ({(compilationCached ? "cached" : "new")})\nC# + references: {source.CSharpMilliseconds:F1} ms\nIL analysis: {source.AnalysisMilliseconds:F1} ms\n"));
            if (request.Action == ShaderAction.Run) {
                if (await TryRunPipelineAsync(source, revision, token)) return;
                if (await TryRunMainAsync(source, revision, token)) return;
            }
            if (source.Entries.Count == 0) {
                _shaderArtifact = null;
                _shaderOutput.Text("Compiled C# · no shader entries");
                if (request.Action == ShaderAction.Run)
                    throw new InvalidOperationException("No runnable entry. Declare Render(in EditorPipelineFrame, ref Hooks), a standard Main/top-level program, or a fragment shader.");
                SetBuildStatus("Compilation succeeded · no shader entries" + PreviousShaderPreview());
                return;
            }
            var kernel = source.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName) ?? source.Entries[0];
            _selectedShaderName = kernel.SelectionKey;
            SetBuildStatus("Writing WGSL / SPIR-V…");
            DomRuntime.Flush();
            var artifactCached = _shaderArtifacts.TryGetValue(kernel.MetadataToken, out var artifact);
            if (artifact is null) {
                artifact = await GetShaderArtifactAsync(source, kernel.MetadataToken, revision, token);
                if (!CurrentShaderJob(revision)) return;
            }
            if (!CurrentShaderJob(revision)) return;
            _shaderArtifact = artifact;
            RefreshShaderArtifact();
            _shaderScope.Text($"{SourceScope()} · {files.Length} C# files including subfolders · snapshot {hash[..8]} · {artifact.QualifiedName}");
            var timings = FormattableString.Invariant(
                $"Compiled snapshot timings (ms) · source {(compilationCached ? "cached" : "new")} · artifact {(artifactCached ? "cached" : "new")}\nC# + references: {source.CSharpMilliseconds:F1}\nIL analysis: {source.AnalysisMilliseconds:F1}\nLowering: {artifact.LoweringMilliseconds:F1}\nWGSL: {artifact.WgslMilliseconds:F1}\nSPIR-V: {artifact.SpirvMilliseconds:F1}\nSPIR-V → WGSL: {artifact.TranslationMilliseconds:F1}\nWGSL: {artifact.Wgsl.Length} chars · SPIR-V: {artifact.Spirv.Length} bytes\n");
            _compilationOutput.Text(timings);
            if (request.Action == ShaderAction.Compile) {
                SetBuildStatus(_shaderPreviewSnapshot is null
                    ? "Shaders compiled · Run to render"
                    : "Compilation succeeded" + PreviousShaderPreview());
                return;
            }
            SetBuildStatus("Creating GPU pipeline…");
            OpenPanelWindow("render");
            DomRuntime.Flush();
            _shaderPreview ??= new("editor-shader-preview", "editor-page-status", _uniformInspector!);
            _shaderPreview.Invalidate(revision);
            var gpuMilliseconds = await _shaderPreview.ShowAsync(artifact, revision);
            if (!CurrentShaderJob(revision) || gpuMilliseconds < 0) return;
            StopPipeline(cancelPending: false);
            _shaderPreviewSnapshot = $"{hash[..8]} · {artifact.QualifiedName}";
            _compilationOutput.Text(timings + FormattableString.Invariant($"GPU pipeline: {gpuMilliseconds:F1} ms\n"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) {
            if (!_disposed) SetBuildStatus("Shader job cancelled");
        }
        catch (Exception error) {
            if (CurrentShaderJob(revision)) {
                ShowOperationError($"Run / compile failed: {error.Message}" + PreviousShaderPreview(), error);
            }
        }
        finally {
            if (!_disposed && token.IsCancellationRequested) SetBuildStatus("Shader job cancelled");
            if (!_disposed) {
                var elapsed = clock.Elapsed.TotalMilliseconds;
                _lastActionStatus.Text(FormattableString.Invariant($"{request.Action} {elapsed:F0} ms"))
                    .Attr("title", FormattableString.Invariant($"{request.Action} · {request.Folder}/**/*.cs · {elapsed:F1} ms wall time, including execution for Run"));
            }
            var next = _shaderJobs.Complete(job);
            if (!_disposed) {
                _buildStatus.ToggleClass("busy", false);
                UpdateActions();
                DomRuntime.Flush();
                if (next is not null) StartShaderOperation(next);
            }
        }
    }

    private void RefreshShaderArtifact()
    {
        if (_shaderArtifact is not { } artifact) return;
        _shaderOutput.Text(_shaderView.Value() switch {
            "spirv" => $"SPIR-V · {artifact.Spirv.Length} bytes · download for the full binary\n" +
                Convert.ToHexString(artifact.Spirv.AsSpan(0, System.Math.Min(artifact.Spirv.Length, 4096))),
            "translated" => artifact.TranslatedWgsl,
            _ => artifact.Wgsl,
        });
    }

    private void StopRendering()
    {
        _shaderPreviewSnapshot = null;
        CancelShaderOperation("Preview closed", clearPending: true);
        try { StopPipeline(cancelPending: false); }
        finally {
            var preview = _shaderPreview;
            _shaderPreview = null;
            try { preview?.Dispose(); }
            finally {
                if (!_disposed && !_runningMain) {
                    SetBuildStatus("Rendering stopped");
                    UpdateActions();
                }
            }
        }
    }
}
