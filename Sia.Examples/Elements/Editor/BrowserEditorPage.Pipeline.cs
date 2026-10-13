using System.Reflection;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
#if BROWSER
    private EditorPipelineSession? _pipeline;
    private (ShaderCompilation Source, Assembly Assembly)? _pipelineProgram;
#endif

    private Task CreatePipelineExampleAsync()
        => OpenShaderExampleAsync(PipelineExample.Folder, "Pipeline.cs", PipelineExample.Source,
            "Pipeline example ready · Run Pipeline · focus canvas, move pointer, press Space");

    private async Task OpenShaderExampleAsync(string folder, string name, string source, string message)
    {
        SaveEditorSources();
        if (!_workspace.TryGetEntity(folder, out _))
            await _workspace.CreateFolderAsync("", folder, _lifetime.Token);
        var path = folder + "/" + name;
        if (!_workspace.TryGetEntity(path, out var file)) {
            file = await _workspace.CreateFileAsync(folder, name, _lifetime.Token);
            _workspace.SetContent(file, source);
            await _workspace.SaveAsync(file, _lifetime.Token);
        }
        if (_disposed) return;
        RenderExplorer();
        await OpenEntryAsync(file, pin: true);
        OpenPanelWindow("render");
        UpdateChrome(message);
    }

    private async Task RunPipelineAsync(ShaderCompilation source, bool spirvRoute, int revision, CancellationToken token)
    {
#if BROWSER
        var artifacts = new Dictionary<int, ShaderArtifact>();
        foreach (var entry in source.Entries) {
            var shader = await GetShaderArtifactAsync(source, entry.MetadataToken, revision, token);
            if (!CurrentShaderJob(revision)) return;
            artifacts.Add(entry.MetadataToken, shader);
        }
        if (_pipelineProgram is not { } program || !ReferenceEquals(source, program.Source)) {
            program = (source, Assembly.Load(source.Image!));
            _pipelineProgram = program;
        }
        var assembly = program.Assembly;
        var methods = assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == "Render" && method.ReturnType == typeof(ReactiveNode)
                && method.GetParameters() is [{ ParameterType: var frame }, { ParameterType: var hooks }]
                && hooks == typeof(Hooks).MakeByRefType() && frame == typeof(EditorPipelineFrame).MakeByRefType()).ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException("Declare exactly one public static ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks) in the active folder.");
        var render = methods[0].CreateDelegate<ReactiveComponent<EditorPipelineFrame>>();
        var session = await EditorPipelineSession.CreateAsync(assembly, artifacts, render, spirvRoute, message => {
            if (!_disposed && revision == _shaderRevision) { _shaderStatus.Text(message); DomRuntime.Flush(); }
        }, token);
        try {
            if (!CurrentShaderJob(revision)) return;
            StopPipeline(cancelPending: false);
            _pipeline = session;
            _shaderPreview?.Dispose();
            _shaderPreview = null;
            _shaderPreviewSnapshot = null;
            _renderOutput.Text("Programmable RenderGraph · focus the canvas for input");
            _buildStatus.Text("Programmable pipeline running");
        }
        finally {
            if (!ReferenceEquals(_pipeline, session)) session.Dispose();
        }
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Programmable pipelines run in the browser editor.");
#endif
    }

    private void StopPipeline(bool cancelPending = true)
    {
        if (cancelPending && _shaderCancellation is not null) CancelShaderOperation("Pipeline stopped");
#if BROWSER
        var pipeline = _pipeline;
        _pipeline = null;
        pipeline?.Dispose();
#endif
    }
}
