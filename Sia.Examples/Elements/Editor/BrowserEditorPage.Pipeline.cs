using System.Reflection;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
#if BROWSER
    private EditorPipelineSession? _pipeline;
#endif

    private async Task<bool> TryRunPipelineAsync(ShaderCompilation source, bool spirvRoute, bool normalCanvas,
        int revision, CancellationToken token)
    {
#if BROWSER
        var assembly = source.Program;
        var methods = assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == "Render" && !method.IsAbstract && !method.ContainsGenericParameters && method.ReturnType == typeof(ReactiveNode)
                && method.GetParameters() is [{ ParameterType: var frame, IsIn: true, IsOut: false }, { ParameterType: var hooks, IsIn: false, IsOut: false }]
                && hooks == typeof(Hooks).MakeByRefType() && frame == typeof(EditorPipelineFrame).MakeByRefType()).ToArray();
        if (methods.Length == 0) return false;
        if (methods.Length != 1)
            throw new InvalidOperationException("Declare exactly one public static ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks) in the active folder.");
        var artifacts = new Dictionary<int, ShaderArtifact>();
        foreach (var entry in source.Entries) {
            var shader = await GetShaderArtifactAsync(source, entry.MetadataToken, revision, token);
            if (!CurrentShaderJob(revision)) return true;
            artifacts.Add(entry.MetadataToken, shader);
        }
        var render = methods[0].CreateDelegate<ReactiveComponent<EditorPipelineFrame>>();
        OpenPanelWindow("render");
        DomRuntime.Flush();
        var previous = _pipeline;
        if (previous is not null) previous.Paused = true;
        EditorPipelineSession? session = null;
        var console = new ProgramConsole();
        try {
            using var capture = console.Capture();
            session = await EditorPipelineSession.CreateAsync(assembly, artifacts, render, spirvRoute, normalCanvas, message => {
                if (!_disposed && revision == _shaderRevision) { _shaderStatus.Text(message); DomRuntime.Flush(); }
            }, _uniformInspector!, console, token);
            if (!CurrentShaderJob(revision)) return true;
            StopPipeline(cancelPending: false);
            _pipeline = session;
            session.ActivateUniforms();
            SetProgramConsole(console);
            _shaderArtifact = source.Entries.FirstOrDefault(entry => entry.SelectionKey == _selectedShaderName) is { } selected
                ? artifacts[selected.MetadataToken] : null;
            if (_shaderArtifact is null) _shaderOutput.Text("Compiled C# · no shader entries");
            else RefreshShaderArtifact();
            _shaderPreview?.Dispose();
            _shaderPreview = null;
            _shaderPreviewSnapshot = null;
            _renderOutput.Text("Programmable RenderGraph · focus the canvas for input");
            _buildStatus.Text("Programmable pipeline running");
        }
        finally {
            if (previous is not null) previous.Paused = false;
            if (session is null || !ReferenceEquals(_pipeline, session)) {
                session?.Dispose();
                console.Dispose();
            }
        }
        return true;
#else
        await Task.CompletedTask;
        return false;
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
