using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private const string SaveAsInputId = "editor-page-save-as-input";

    private DomElement _confirmBanner = null!;
    private DomElement _confirmMessage = null!;
    private DomElement _confirmActions = null!;
    private readonly List<DomElement> _confirmButtons = [];

    private TaskCompletionSource<CloseDecision>? _closeDecision;
    private Entity? _pendingDeleteEntity;

    private enum CloseDecision
    {
        Save,
        DontSave,
        Cancel,
    }

    private void InitDialogs()
    {
        _confirmBanner = Own(DomElement.Create("div")
            .Class("editor-page-confirm-banner")
            .Class("hidden"));
        _confirmMessage = Own(DomElement.Create("span").Class("editor-page-confirm-message"));
        _confirmActions = Own(DomElement.Create("div").Class("editor-page-confirm-actions"));
        _confirmBanner.Append(_confirmMessage).Append(_confirmActions);
    }

    private Task<CloseDecision> RequestCloseDecisionAsync(Entity entity)
    {
        _closeDecision?.TrySetResult(CloseDecision.Cancel);
        _closeDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ShowConfirmBanner(
            $"'{EditorWorkspace.NameOf(entity)}' has unsaved changes. Save before closing?",
            ("Save", "editor-page-dialog-save"),
            ("Don't Save", "editor-page-dialog-dont-save"),
            ("Cancel", "editor-page-dialog-cancel"));
        return _closeDecision.Task;
    }

    private void ResolveCloseDialog(CloseDecision decision)
    {
        HideConfirmBanner();
        _closeDecision?.TrySetResult(decision);
        _closeDecision = null;
    }

    private void CancelCloseDialog() => ResolveCloseDialog(CloseDecision.Cancel);

    private async Task<bool> SaveEntityAsync(Entity entity)
    {
        if (entity.Contains<UntitledFile>()) {
            _activeEntity = entity;
            BeginSaveAs();
            return false;
        }
        try {
            await _workspace.SaveAsync(entity, _lifetime.Token);
            return true;
        }
        catch (WorkspaceConflictException error) {
            _consoleOutput.Text(
                $"Could not save '{EditorWorkspace.NameOf(entity)}': {error.Message}\n").ToggleClass(
                "output-error", true);
            return false;
        }
    }

    private async Task SaveActiveAsync()
    {
        if (_activeEntity is not { } entity || !entity.IsValid) {
            return;
        }
        SaveEditorSources();
        if (await SaveEntityAsync(entity)) {
            ApplyLayout();
            UpdateChrome("saved");
        }
    }

    private async Task SaveAllAsync()
    {
        SaveEditorSources();
        var untitled = _workspace.OpenFiles().Where(static e => e.Contains<UntitledFile>() && EditorWorkspace.IsDirty(e)).ToArray();
        var failures = await _workspace.SaveAllAsync(_lifetime.Token);
        ApplyLayout();
        UpdateChrome(failures.Count == 0 && untitled.Length == 0
            ? "saved all"
            : $"saved · {failures.Count} failed, {untitled.Length} untitled file(s) need Save As");
    }

    private void BeginSaveAs()
    {
        if (_activeEntity is not { } entity || !entity.IsValid) {
            return;
        }
        var currentPath = entity.Contains<UntitledFile>()
            ? EditorWorkspace.NameOf(entity)
            : EditorWorkspace.PathOf(entity);

        ClearConfirmActions();
        _confirmMessage.Text("Save as (workspace-relative path):");
        var input = DomElement.Create("input")
            .Class("inline-title-input")
            .Id(SaveAsInputId)
            .Attr("type", "text")
            .Attr("aria-label", "New file path")
            .Attr("value", currentPath)
            .Attr("data-inline-input", "true")
            .Attr("data-saved-value", currentPath);
        var save = DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("data-inline-save", SaveAsInputId)
            .On("click", "editor-page-save-as-commit")
            .Text("Save");
        var cancel = DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .On("click", "editor-page-save-as-cancel")
            .Text("Cancel");
        _confirmButtons.Add(input);
        _confirmButtons.Add(save);
        _confirmButtons.Add(cancel);
        _confirmActions.Append(input).Append(save).Append(cancel);
        _confirmBanner.ToggleClass("hidden", false);
    }

    private async Task CommitSaveAsAsync()
    {
        if (_activeEntity is not { } entity || !entity.IsValid) {
            return;
        }
        string? path;
        using (var input = DomElement.TryFind(SaveAsInputId)) {
            path = input?.Value().Trim();
        }
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        HideConfirmBanner();
        SaveEditorSources();
        Entity saved;
        try {
            saved = await _workspace.SaveAsAsync(entity, path, _lifetime.Token);
        }
        catch (Exception error) when (error is WorkspaceEntryExistsException or ArgumentException) {
            _consoleOutput.Text($"Could not save as '{path}': {error.Message}\n").ToggleClass("output-error", true);
            return;
        }
        if (saved != entity) {
            EnsureFileWindow(saved);
            _activeEntity = saved;
            UpdateState(state => NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(saved)));
        } else {
            ApplyLayout();
            UpdateChrome("saved");
        }
        RenderExplorer();
        RefreshExplorerSelection();
    }

    private void CancelSaveAs() => HideConfirmBanner();

    private void BeginDelete(string key)
    {
        if (!TryResolve(key, out var entity) || !entity.IsValid) {
            return;
        }
        _pendingDeleteEntity = entity;
        var name = EditorWorkspace.NameOf(entity);
        var noun = EditorWorkspace.IsFolder(entity) ? "folder (and everything inside it)" : "file";
        ShowConfirmBanner(
            $"Delete the {noun} '{name}'? This cannot be undone.",
            ("Delete", "editor-page-dialog-delete-confirm"),
            ("Cancel", "editor-page-dialog-delete-cancel"));
    }

    private void CancelDeleteDialog()
    {
        _pendingDeleteEntity = null;
        HideConfirmBanner();
    }

    private async Task ConfirmDeleteAsync()
    {
        HideConfirmBanner();
        if (_pendingDeleteEntity is not { } entity || !entity.IsValid) {
            return;
        }
        _pendingDeleteEntity = null;

        var deletedPath = EditorWorkspace.PathOf(entity);
        foreach (var open in _workspace.OpenFiles().ToArray()) {
            if (!open.IsValid) {
                continue;
            }
            var isTarget = open == entity;
            var isDescendant = !open.Contains<UntitledFile>()
                && WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(open), deletedPath);
            if (isTarget || isDescendant) {
                FinishCloseTab(open, TabIdFor(open), WindowIdFor(open));
            }
        }
        await _workspace.DeleteAsync(entity, _lifetime.Token);
        Forget(entity);
        RenderExplorer();
    }

    private void ShowConfirmBanner(string message, params (string Label, string Payload)[] actions)
    {
        ClearConfirmActions();
        _confirmMessage.Text(message);
        foreach (var (label, payload) in actions) {
            var button = DomElement.Create("button")
                .Class("btn")
                .Attr("type", "button")
                .On("click", payload)
                .Text(label);
            _confirmButtons.Add(button);
            _confirmActions.Append(button);
        }
        _confirmBanner.ToggleClass("hidden", false);
    }

    private void ClearConfirmActions()
    {
        _confirmActions.Text(string.Empty);
        foreach (var button in _confirmButtons) {
            button.Dispose();
        }
        _confirmButtons.Clear();
    }

    private void HideConfirmBanner()
    {
        ClearConfirmActions();
        _confirmBanner.ToggleClass("hidden", true);
    }
}
