using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private const string k_SaveAsInputId = "editor-page-save-as-input";

    private DomElement _confirmBanner = null!;
    private DomElement _confirmMessage = null!;
    private DomElement _confirmActions = null!;
    private readonly List<DomElement> _confirmElements = [];

    private TaskCompletionSource<(CloseDecision Decision, string Content)>? _closeDecision;
    private (Entity Entity, CellTab? Tab, string Path)? _dialog;
    private long _dialogGeneration;

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

    private Task<(CloseDecision Decision, string Content)> RequestCloseDecisionAsync(Entity entity)
    {
        BeginDialog(entity);
        _closeDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ShowConfirmBanner(
            $"'{EditorWorkspace.NameOf(entity)}' has unsaved changes. Save before closing?",
            ("Save", "editor-page-dialog-save"),
            ("Don't Save", "editor-page-dialog-dont-save"),
            ("Cancel", "editor-page-dialog-cancel"));
        return _closeDecision.Task;
    }

    private void ResolveCloseDialog(CloseDecision decision, string argument)
    {
        if (!IsCurrentDialog(argument) || _closeDecision is not { } pending) {
            return;
        }
        SaveEditorSources(synchronizeNativeInput: true);
        var entity = _dialog!.Value.Entity;
        var content = entity.Get<WorkspaceFile>().Content;
        _closeDecision = null;
        HideConfirmBanner();
        if (decision == CloseDecision.Cancel && _activeEntity == entity && ActiveLanguageGroup(entity) is { } group)
            _editors.Focus(group);
        pending.TrySetResult((decision, content));
    }

    private void BeginDialog(Entity entity)
    {
        CancelCloseDialog();
        _state.Tabs.TryGetValue(TabIdFor(entity), out var tab);
        _dialog = (entity, tab, EditorWorkspace.PathOf(entity));
    }

    private bool IsCurrentDialog(string argument)
    {
        if (!long.TryParse(argument, out var generation) || generation != _dialogGeneration) {
            return false;
        }
        if (_dialog is { } owner && owner.Entity.IsValid
            && EditorWorkspace.PathOf(owner.Entity) == owner.Path
            && (owner.Tab is null || IsCurrentTab(owner.Entity, owner.Tab))) {
            return true;
        }
        CancelCloseDialog();
        return false;
    }

    private void CancelDialog(string argument, bool restoreEditor = false)
    {
        if (IsCurrentDialog(argument)) {
            var entity = _dialog!.Value.Entity;
            CancelCloseDialog();
            if (restoreEditor && _activeEntity == entity && ActiveLanguageGroup(entity) is { } group)
                _editors.Focus(group);
        }
    }

    private void CancelDialog(Entity entity)
    {
        if (_dialog is { } owner && owner.Entity == entity) {
            CancelCloseDialog();
        }
    }

    private void CancelCloseDialog()
    {
        var pending = _closeDecision;
        _closeDecision = null;
        HideConfirmBanner();
        pending?.TrySetResult((CloseDecision.Cancel, string.Empty));
    }

    internal (string Content, bool Dirty) CaptureNavigationState()
    {
        SaveEditorSources(synchronizeNativeInput: true);
        var files = _workspace.OpenFiles();
        var content = System.Text.Json.JsonSerializer.Serialize(files
            .OrderBy(EditorWorkspace.PathOf)
            .Select(static entity => new {
                Path = EditorWorkspace.PathOf(entity),
                entity.Get<WorkspaceFile>().Content,
            }));
        return (content, files.Any(EditorWorkspace.IsDirty));
    }

    internal async Task<bool> SaveForNavigationAsync()
    {
        CancelCloseDialog();
        SaveEditorSources(synchronizeNativeInput: true);
        foreach (var entity in _workspace.OpenFiles()) {
            if (EditorWorkspace.IsDirty(entity) && !await SaveEntityAsync(entity)) {
                return false;
            }
        }
        UpdateChrome("saved all");
        return true;
    }

    private async Task<bool> SaveEntityAsync(Entity entity)
    {
        if (!entity.IsValid || !_state.Tabs.TryGetValue(TabIdFor(entity), out var tab)) {
            return false;
        }
        if (entity.Contains<UntitledFile>()) {
            BeginSaveAs(entity);
            return false;
        }
        var path = EditorWorkspace.PathOf(entity);
        var name = EditorWorkspace.NameOf(entity);
        try {
            await _workspace.SaveAsync(entity, _lifetime.Token);
            return IsCurrentTab(entity, tab) && EditorWorkspace.PathOf(entity) == path;
        }
        catch (Exception) when (!IsCurrentTab(entity, tab) || EditorWorkspace.PathOf(entity) != path) {
            return false;
        }
        catch (WorkspaceConflictException error) {
            ShowOperationError($"Could not save '{name}': {error.Message}", error);
            return false;
        }
    }

    private async Task SaveActiveAsync()
    {
        if (_activeEntity is not { } entity || !entity.IsValid) {
            return;
        }
        _state.Tabs.TryGetValue(TabIdFor(entity), out var tab);
        var activation = _activationGeneration;
        SaveEditorSources(synchronizeNativeInput: true);
        var saved = await SaveEntityAsync(entity);
        if (tab is not null && IsCurrentTab(entity, tab)) {
            ApplyLayout();
            if (_activeEntity == entity) {
                UpdateChrome(saved && activation == _activationGeneration ? "saved" : null);
            }
        }
    }

    private async Task SaveAllAsync()
    {
        SaveEditorSources(synchronizeNativeInput: true);
        var untitled = _workspace.OpenFiles().Where(static e => e.Contains<UntitledFile>() && EditorWorkspace.IsDirty(e)).ToArray();
        var failures = await _workspace.SaveAllAsync(_lifetime.Token);
        ApplyLayout();
        if (failures.Count != 0) ShowOperationError($"Could not save {failures.Count} file(s)",
            new AggregateException(failures.Select(static failure => failure.Error)));
        UpdateChrome(failures.Count == 0 && untitled.Length == 0
            ? "saved all"
            : $"saved · {failures.Count} failed, {untitled.Length} untitled file(s) need Save As");
    }

    private void BeginSaveAs()
    {
        if (_activeEntity is { } entity && entity.IsValid) {
            BeginSaveAs(entity);
        }
    }

    private void BeginSaveAs(Entity entity)
    {
        if (!entity.IsValid || !entity.Contains<OpenEditorFile>()) {
            return;
        }
        var currentPath = entity.Contains<UntitledFile>()
            ? EditorWorkspace.NameOf(entity) + ".cs"
            : EditorWorkspace.PathOf(entity);

        BeginDialog(entity);
        _confirmMessage.Text("Save as (workspace-relative path):");
        var input = DomElement.Create("input")
            .Id(k_SaveAsInputId)
            .Attr("type", "text")
            .Attr("aria-label", "New file path")
            .Attr("value", currentPath)
            .Attr("data-inline-input", "true")
            .Attr("data-saved-value", currentPath);
        var save = DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("data-inline-save", k_SaveAsInputId)
            .On("click", $"editor-page-save-as-commit:{_dialogGeneration}")
            .Text("Save");
        var cancel = DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("data-inline-discard", k_SaveAsInputId)
            .On("click", $"editor-page-save-as-cancel:{_dialogGeneration}")
            .Text("Cancel");
        _confirmElements.Add(input);
        _confirmElements.Add(save);
        _confirmElements.Add(cancel);
        _confirmActions.Append(input);
        CreateConfirmButtons().Append(save).Append(cancel);
        _confirmBanner.ToggleClass("hidden", false);
        input.Focus();
    }

    private async Task CommitSaveAsAsync(string argument)
    {
        if (!IsCurrentDialog(argument)) {
            return;
        }
        var owner = _dialog!.Value;
        var entity = owner.Entity;
        var activation = _activationGeneration;
        string? path;
        using (var input = DomElement.TryFind(k_SaveAsInputId)) {
            path = input?.Value().Trim();
        }
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        HideConfirmBanner();
        var dialogGeneration = _dialogGeneration;
        SaveEditorSources(synchronizeNativeInput: true);
        Entity saved;
        try {
            saved = await _workspace.SaveAsAsync(entity, path, _lifetime.Token);
            InvalidateLanguageContext();
            ScheduleLanguageDiagnostics(ActiveLanguageBinding());
        }
        catch (Exception error) when (error is WorkspaceEntryExistsException or ArgumentException) {
            ShowOperationError($"Could not save as '{path}': {error.Message}", error);
            return;
        }
        if (_disposed) {
            return;
        }
        var isCurrent = activation == _activationGeneration && dialogGeneration == _dialogGeneration;
        if (saved != entity && owner.Tab is { } tab && IsCurrentTab(entity, tab)
            && _activeEntity == entity && isCurrent) {
            ++_activationGeneration;
            EnsureFileWindow(saved);
            _activeEntity = saved;
            UpdateState(state => NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(saved)));
        }
        else {
            ApplyLayout();
            UpdateChrome("saved");
        }
        RenderExplorer();
        RefreshExplorerSelection();
        if (isCurrent && _activeEntity == saved && ActiveLanguageGroup(saved) is { } group) _editors.Focus(group);
    }

    private void BeginDelete(string key)
    {
        if (!TryResolve(key, out var entity) || !entity.IsValid) {
            return;
        }
        BeginDialog(entity);
        var name = EditorWorkspace.NameOf(entity);
        var noun = EditorWorkspace.IsFolder(entity) ? "folder (and everything inside it)" : "file";
        ShowConfirmBanner(
            $"Delete the {noun} '{name}'? This cannot be undone.",
            ("Delete", "editor-page-dialog-delete-confirm"),
            ("Cancel", "editor-page-dialog-delete-cancel"));
        _confirmElements[^1].Focus();
    }

    private async Task ConfirmDeleteAsync(string argument)
    {
        if (!IsCurrentDialog(argument)) {
            return;
        }
        var entity = _dialog!.Value.Entity;
        HideConfirmBanner();

        SaveEditorSources(synchronizeNativeInput: true);
        var deletedPath = EditorWorkspace.PathOf(entity);
        var approved = _workspace.OpenFiles().ToDictionary(
            static file => file,
            file => (Content: file.Get<WorkspaceFile>().Content,
                Tab: _state.Tabs.GetValueOrDefault(TabIdFor(file))));
        CancelLanguageDiagnostics();
        await _workspace.DeleteEntryAsync(entity, _lifetime.Token);
        InvalidateLanguageContext();
        if (_disposed) {
            return;
        }
        SaveEditorSources(synchronizeNativeInput: true);
        foreach (var open in _workspace.OpenFiles().ToArray()) {
            if (!open.IsValid) {
                continue;
            }
            var isTarget = open == entity && !open.Contains<UntitledFile>()
                && EditorWorkspace.PathOf(open) == deletedPath;
            var isDescendant = !open.Contains<UntitledFile>()
                && WorkspacePath.IsDescendantOf(EditorWorkspace.PathOf(open), deletedPath);
            if (isTarget || isDescendant) {
                if (!approved.TryGetValue(open, out var snapshot)
                    || open.Get<WorkspaceFile>().Content != snapshot.Content
                    || snapshot.Tab is null || !IsCurrentTab(open, snapshot.Tab)) {
                    CancelDialog(open);
                    _workspace.RetainDeletedDraft(open);
                }
                else {
                    FinishCloseTab(open, TabIdFor(open), WindowIdFor(open));
                }
            }
        }
        if (entity.IsValid && !entity.Contains<UntitledFile>() && EditorWorkspace.PathOf(entity) == deletedPath) {
            _workspace.RemoveEntry(entity);
        }
        foreach (var removed in _entryKeys.Keys.Where(static entry => !entry.IsValid).ToArray()) {
            Forget(removed);
        }
        ApplyLayout();
        UpdateChrome();
        RenderExplorer();
        ScheduleLanguageDiagnostics(ActiveLanguageBinding());
    }

    private void ShowConfirmBanner(string message, params (string Label, string Payload)[] actions)
    {
        ClearConfirmActions();
        _confirmMessage.Text(message);
        var buttons = CreateConfirmButtons();
        foreach (var (label, payload) in actions) {
            var button = DomElement.Create("button")
                .Class("btn")
                .Attr("type", "button")
                .On("click", $"{payload}:{_dialogGeneration}")
                .Text(label);
            _confirmElements.Add(button);
            buttons.Append(button);
        }
        _confirmBanner.ToggleClass("hidden", false);
    }

    private DomElement CreateConfirmButtons()
    {
        var buttons = DomElement.Create("div").Class("editor-page-confirm-buttons");
        _confirmElements.Add(buttons);
        _confirmActions.Append(buttons);
        return buttons;
    }

    private void ClearConfirmActions()
    {
        _confirmActions.Text(string.Empty);
        foreach (var element in _confirmElements) {
            element.Dispose();
        }
        _confirmElements.Clear();
    }

    private void HideConfirmBanner()
    {
        ++_dialogGeneration;
        _dialog = null;
        ClearConfirmActions();
        _confirmBanner.ToggleClass("hidden", true);
    }
}
