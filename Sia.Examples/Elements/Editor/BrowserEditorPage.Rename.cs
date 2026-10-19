using Sia;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private EditorWorkspaceEdits _workspaceEdits = null!;
    private EditorRenamePanel _renamePanel = null!;

    private void InitializeRenameTools()
    {
        _workspaceEdits = new(CaptureWorkspaceBuffers, CommitWorkspaceBuffers, message => _renamePanel.Report(message));
        _renamePanel = new(_editors, _workspaceEdits, "editor-language-rename", () => {
            if (ActiveLanguageBinding() is { } binding && _editors.TryResolveCellId(binding, out var group)) _editors.Focus(group);
        });
        _editors.RenameReady += (binding, target, source, position) => _renamePanel.Open(binding, target, source, position);
    }

    private IEnumerable<Entity> LanguageFiles(Entity active)
    {
        var activeId = EditorWorkspace.PathOf(active);
        var folder = WorkspacePath.Parent(activeId) ?? "";
        return _workspace.AllFiles().Where(file => file == active || !file.Contains<UntitledFile>()
            && EditorWorkspace.PathOf(file).EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && (folder.Length == 0 || WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(file), folder)));
    }

    private IReadOnlyDictionary<string, WorkspaceEditBuffer> CaptureWorkspaceBuffers(RenamePlan plan)
    {
        var active = _workspace.AllFiles().FirstOrDefault(file => EditorWorkspace.PathOf(file) == plan.Origin);
        var buffers = new Dictionary<string, WorkspaceEditBuffer>(StringComparer.Ordinal);
        if (!active.IsValid) return buffers;
        foreach (var file in LanguageFiles(active)) {
            var path = EditorWorkspace.PathOf(file);
            var mounted = false;
            foreach (var (group, entity) in _editorEntities) {
                if (entity != file || !_editors.TryCaptureWorkspaceBuffer(group, out var buffer)) continue;
                buffers.Add(path, buffer); mounted = true; break;
            }
            if (mounted) continue;
            var position = _mementos.TryGetValue(file, out var memento) ? memento : new EditorMemento(EditorSelection.Single(0), 0, 0, 0, 0, 0);
            buffers.Add(path, new(file.Get<WorkspaceFile>().Content, position));
        }
        return buffers;
    }

    private void CommitWorkspaceBuffers(IReadOnlyList<WorkspaceBufferChange> changes)
    {
        var files = changes.Select(change => (Change: change, Entity: _workspace.AllFiles().First(file => EditorWorkspace.PathOf(file) == change.DocumentId))).ToArray();
        foreach (var (change, entity) in files) _workspace.SetContent(entity, change.After.Source);
        foreach (var (change, entity) in files) {
            var mounted = false;
            foreach (var (group, current) in _editorEntities) {
                if (current != entity) continue;
                mounted |= _editors.ApplyWorkspaceChange(group, change);
            }
            _mementos[entity] = mounted ? change.After.Position with { History = null } : change.After.Position;
        }
        SyncDirtyIndicators();
    }
}
