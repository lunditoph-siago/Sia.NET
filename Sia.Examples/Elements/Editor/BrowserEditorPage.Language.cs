using Sia;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private async ValueTask<CSharpProjectSnapshot> CaptureLanguageSnapshotAsync(string bindingId,
        string source, CSharpQueryKind kind, CancellationToken cancellationToken)
    {
        await _shaderCompiler.PrepareReferencesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_editors.TryResolveCellId(bindingId, out var groupId)
            || !_editorEntities.TryGetValue(groupId, out var active) || !active.IsValid)
            throw new OperationCanceledException(cancellationToken);
        var activeId = EditorWorkspace.PathOf(active);
        var files = LanguageFiles(active).ToArray();
        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.Contains<UntitledFile>()) await _workspace.EnsureContentLoadedAsync(file, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var documents = new List<CSharpSourceDocument>(files.Length);
        foreach (var file in files) {
            if (!file.IsValid) continue;
            var path = EditorWorkspace.PathOf(file);
            var content = file.Get<WorkspaceFile>().Content;
            foreach (var (group, entity) in _editorEntities) {
                if (entity == file && _editors.TryGetSource(group, out var live)) { content = live; break; }
            }
            documents.Add(new(path, file.Contains<UntitledFile>() ? "Untitled.cs" : path, path,
                file == active ? source : content));
        }
        return new(activeId, documents) {
            OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            SelectOutputKind = EditorShaderCompiler.ChooseOutputKind
        };
    }
}
