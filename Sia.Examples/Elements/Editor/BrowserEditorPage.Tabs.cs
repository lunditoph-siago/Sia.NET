using System.Globalization;
using Sia.Examples.Notebook;
using Sia;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly Dictionary<Entity, string> _entryKeys = [];
    private readonly Dictionary<string, Entity> _entriesByKey = [];
    private int _entryKeySeed;
    private Entity? _activeEntity;
    private long _activationGeneration;

    private string KeyFor(Entity entity)
    {
        if (_entryKeys.TryGetValue(entity, out var key)) {
            return key;
        }
        key = "e" + (++_entryKeySeed).ToString(CultureInfo.InvariantCulture);
        _entryKeys[entity] = key;
        _entriesByKey[key] = entity;
        return key;
    }

    private bool TryResolve(string key, out Entity entity) => _entriesByKey.TryGetValue(key, out entity);

    private void Forget(Entity entity)
    {
        _mementos.Remove(entity);
        if (_entryKeys.Remove(entity, out var key)) {
            _entriesByKey.Remove(key);
        }
    }

    private string WindowIdFor(Entity entity) => $"window-{KeyFor(entity)}-{k_ScriptSuffix}";

    private string TabIdFor(Entity entity) => $"tab-{KeyFor(entity)}-{k_ScriptSuffix}";

    private async Task OpenEntryAsync(string key, bool pin)
    {
        if (!TryResolve(key, out var entity)) {
            return;
        }
        await OpenEntryAsync(entity, pin);
    }

    private async Task OpenEntryAsync(Entity entity, bool pin, bool showConsole = false)
    {
        if (!entity.IsValid || EditorWorkspace.IsFolder(entity)) {
            return;
        }
        var generation = ++_activationGeneration;
        if (!await _workspace.EnsureContentLoadedAsync(entity, _lifetime.Token)
            || _disposed || generation != _activationGeneration) {
            return;
        }
        await _workspace.OpenFileAsync(entity, pin, _lifetime.Token);
        EnsureFileWindow(entity);
        _activeEntity = entity;
        UpdateState(state => {
            var opened = NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(entity));
            return showConsole ? NotebookCellLayout.OpenWindow(opened, k_ConsoleWindowId) : opened;
        });
        RefreshExplorerSelection();
        ScheduleLanguageDiagnostics(ActiveLanguageBinding());
    }

    private void ActivateTab(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)) {
            return;
        }
        ++_activationGeneration;
        var window = _state.Windows[tab.WindowId];
        if (window.Kind == CellWindowKind.Script && TryResolve(window.SourceId, out var entity)) {
            _activeEntity = entity;
        }
        UpdateState(state => NotebookCellLayout.Activate(state, tabId));
        RefreshExplorerSelection();
        ScheduleLanguageDiagnostics(ActiveLanguageBinding());
    }

    private void OpenPanelWindow(string windowKey)
        => UpdateState(state => NotebookCellLayout.OpenWindow(
            state,
            windowKey switch {
                "compilation" => k_CompilationWindowId,
                "console" => k_ConsoleWindowId,
                "render" => k_RenderWindowId,
                _ => throw new ArgumentOutOfRangeException(nameof(windowKey)),
            }));

    private void CloseActiveTab()
    {
        var active = FindActiveTabId();
        if (active is not null) {
            Track(CloseTabAsync(active));
        }
    }

    private string? FindActiveTabId()
    {
        var active = NotebookCellLayout.EnumerateGroups(_state)
            .Select(static group => group.ActiveTabId).ToArray();
        if (_activeEntity is { } entity && entity.IsValid) {
            var tabId = TabIdFor(entity);
            if (active.Contains(tabId)) {
                return tabId;
            }
        }
        return active.FirstOrDefault(id => _state.Tabs.ContainsKey(id));
    }

    private async Task CloseTabAsync(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)
            || !_state.Windows.TryGetValue(tab.WindowId, out var window)) {
            return;
        }
        if (window.Kind != CellWindowKind.Script) {
            if (window.Id == k_RenderWindowId) StopRendering();
            UpdateState(state => NotebookCellLayout.CloseTab(state, tabId));
            return;
        }
        if (!TryResolve(window.SourceId, out var entity) || !entity.IsValid) {
            return;
        }
        while (IsCurrentTab(entity, tab)) {
            SaveEditorSources(synchronizeNativeInput: true);
            if (!EditorWorkspace.IsDirty(entity)) {
                break;
            }
            var (choice, content) = await RequestCloseDecisionAsync(entity);
            if (!IsCurrentTab(entity, tab) || choice == CloseDecision.Cancel) {
                return;
            }
            SaveEditorSources(synchronizeNativeInput: true);
            if (choice == CloseDecision.DontSave) {
                if (entity.Get<WorkspaceFile>().Content == content) {
                    break;
                }
            }
            else {
                if (!await SaveEntityAsync(entity) || !IsCurrentTab(entity, tab)) {
                    return;
                }
            }
        }
        if (IsCurrentTab(entity, tab)) {
            FinishCloseTab(entity, tabId, window.Id);
        }
    }

    private bool IsCurrentTab(Entity entity, CellTab tab)
        => !_disposed && entity.IsValid
            && _state.Tabs.TryGetValue(tab.Id, out var current) && ReferenceEquals(current, tab);

    private void FinishCloseTab(Entity entity, string tabId, string windowId)
    {
        CancelDialog(entity);
        var destroyed = _workspace.CloseFile(entity);
        _state = NotebookCellLayout.CloseTab(_state, tabId);
        _state = _state with {
            Windows = _state.Windows.Remove(windowId),
            Tabs = _state.Tabs.Remove(tabId),
        };
        if (_activeEntity == entity) {
            ++_activationGeneration;
            _activeEntity = null;
            foreach (var group in NotebookCellLayout.EnumerateGroups(_state)) {
                if (_state.Tabs.TryGetValue(group.ActiveTabId, out var active)
                    && _state.Windows.TryGetValue(active.WindowId, out var next)
                    && next.Kind == CellWindowKind.Script
                    && TryResolve(next.SourceId, out var remaining)
                    && remaining.IsValid && remaining.Contains<OpenEditorFile>()) {
                    _activeEntity = remaining;
                    break;
                }
            }
        }
        if (destroyed) {
            Forget(entity);
            RenderExplorer();
        }
        ApplyLayout();
        if (_mementos.TryGetValue(entity, out var position)) {
            _mementos[entity] = position with { History = null };
        }
        UpdateChrome();
        RefreshExplorerSelection();
    }
}
