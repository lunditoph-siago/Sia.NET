using Sia;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private async ValueTask<CSharpProjectSnapshot> CaptureLanguageSnapshotAsync(string bindingId,
        string source, CSharpQueryKind kind, CancellationToken cancellationToken)
    {
        var symbols = kind is CSharpQueryKind.DocumentSymbols or CSharpQueryKind.WorkspaceSymbols;
        if (!symbols) {
            await EnsureEditorPackagesAsync(cancellationToken);
            await _shaderCompiler.PrepareReferencesAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!_editors.TryResolveCellId(bindingId, out var groupId)
            || !_editorEntities.TryGetValue(groupId, out var active) || !active.IsValid)
            throw new OperationCanceledException(cancellationToken);
        var activeId = EditorWorkspace.PathOf(active);
        var files = kind switch {
            CSharpQueryKind.DocumentSymbols => [active],
            CSharpQueryKind.WorkspaceSymbols => _workspace.AllFiles().Where(file => file.Contains<UntitledFile>()
                || EditorWorkspace.PathOf(file).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToArray(),
            _ => LanguageFiles(active).ToArray()
        };
        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.Contains<UntitledFile>()) await _workspace.EnsureContentLoadedAsync(file, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var documents = new List<CSharpSourceDocument>(files.Length);
        foreach (var file in files) {
            if (!file.IsValid) continue;
            var path = EditorWorkspace.PathOf(file);
            documents.Add(new(path, file.Contains<UntitledFile>() ? "Untitled.cs" : path, path,
                file == active ? source : ReadLanguageSource(file, synchronize: symbols)));
        }
        return new(activeId, documents) {
            OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            SelectOutputKind = EditorShaderCompiler.ChooseOutputKind
        };
    }

    private string ReadLanguageSource(Entity file, bool synchronize = false)
    {
        foreach (var (group, entity) in _editorEntities) {
            if (entity != file) continue;
            if (synchronize) _editors.SynchronizeNativeInput(group);
            if (_editors.TryGetSource(group, out var live)) return live;
        }
        return file.Get<WorkspaceFile>().Content;
    }
}
