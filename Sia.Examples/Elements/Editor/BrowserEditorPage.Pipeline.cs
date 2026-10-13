using System.Reflection;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private EditorPipelineSession? _pipeline;
    private ShaderCompilation? _pipelineSource;
    private Assembly? _pipelineAssembly;

    private async Task CreatePipelineExampleAsync()
    {
        SaveEditorSources();
        if (!_workspace.TryGetEntity(PipelineExample.Folder, out _))
            await _workspace.CreateFolderAsync("", PipelineExample.Folder, _lifetime.Token);
        var path = PipelineExample.Folder + "/Pipeline.cs";
        if (!_workspace.TryGetEntity(path, out var file)) {
            file = await _workspace.CreateFileAsync(PipelineExample.Folder, "Pipeline.cs", _lifetime.Token);
            _workspace.SetContent(file, PipelineExample.Source);
            await _workspace.SaveAsync(file, _lifetime.Token);
        }
        if (_disposed) return;
        RenderExplorer();
        await OpenEntryAsync(file, pin: true);
        OpenPanelWindow("render");
        UpdateChrome("Pipeline example ready · Run Pipeline · focus canvas, move pointer, press Space");
    }

    private async Task RunPipelineAsync(ShaderCompilation source, bool spirvRoute, int revision, CancellationToken token)
    {
        var artifacts = new Dictionary<int, ShaderArtifact>();
        foreach (var entry in source.Entries) {
            if (!_shaderArtifacts.TryGetValue(entry.MetadataToken, out var shader))
                shader = await Task.Run(() => EditorShaderCompiler.Write(source, entry.MetadataToken, token), token);
            if (!CurrentShaderJob(revision)) return;
            artifacts.Add(entry.MetadataToken, shader);
            if (!_shaderArtifacts.ContainsKey(entry.MetadataToken)) {
                if (_shaderArtifacts.Count >= 8) _shaderArtifacts.Remove(_shaderArtifacts.Keys.First());
                _shaderArtifacts.Add(entry.MetadataToken, shader);
            }
        }
        if (!ReferenceEquals(source, _pipelineSource)) {
            _pipelineAssembly = Assembly.Load(source.Image!);
            _pipelineSource = source;
        }
        var assembly = _pipelineAssembly!;
        var methods = assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == "Render" && method.ReturnType == typeof(ReactiveNode)
                && method.GetParameters() is [{ ParameterType: var frame }, { ParameterType: var hooks }]
                && hooks == typeof(Hooks).MakeByRefType() && frame == typeof(EditorPipelineFrame).MakeByRefType()).ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException("Declare exactly one public static ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks) in the active folder.");
        var render = methods[0].CreateDelegate<ReactiveComponent<EditorPipelineFrame>>();
        var session = new EditorPipelineSession(assembly, artifacts, render, spirvRoute, message => {
            if (!_disposed && revision == _shaderRevision) { _shaderStatus.Text(message); DomRuntime.Flush(); }
        });
        try {
            await session.StartAsync(token);
            if (!CurrentShaderJob(revision)) { session.Dispose(); return; }
            StopPipeline(cancelPending: false);
            _pipeline = session;
            _shaderPreview?.Dispose();
            _shaderPreview = null;
            _shaderPreviewSnapshot = null;
            _renderOutput.Text("Programmable RenderGraph · focus the canvas for input");
            _buildStatus.Text("Programmable pipeline running");
        }
        catch { session.Dispose(); throw; }
    }

    private void StopPipeline(bool cancelPending = true)
    {
        if (cancelPending && _shaderCancellation is not null) CancelShaderOperation("Pipeline stopped");
        var pipeline = _pipeline;
        _pipeline = null;
        pipeline?.Dispose();
    }
}
