using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private DomElement _languageResults = null!;
    private EditorReferenceResults _referenceResults = null!;
    private readonly List<DomElement> _languageElements = [];
    private IReadOnlyList<SourcePosition> _definitionTargets = [];
    private int _definitionGeneration;
    private readonly List<NavigationBookmark> _navigationBack = [];
    private readonly List<NavigationBookmark> _navigationForward = [];
    private SourcePosition? _generatedNavigationTarget;
    private readonly record struct NavigationBookmark(Entity Entity, EditorMemento Position, SourcePosition? Generated = null);

    private void InitializeLanguageTools()
    {
        InitializeRenameTools();
        InitializeNavigationPicker();
        _languageResults = Own(DomElement.Create("section").Class("editor-language-results").Class("hidden"));
        _compilationPanel.InsertBefore(_languageResults, _diagnostics);
        _referenceResults = new("editor-language-reference", target => Track(NavigateToSourceAsync(target)), () => {
            if (ActiveLanguageBinding() is { } binding && _editors.TryResolveCellId(binding, out var group)) _editors.Focus(group);
        });
        _compilationPanel.InsertBefore(_referenceResults.Element, _languageResults);
        _editors.SourceChanged += HandleLanguageSourceChanged;
        _editors.DefinitionReady += HandleDefinitionReady;
        _editors.ReferencesReady += HandleReferencesReady;
    }

    private void HandleReferencesReady(string binding, SymbolReferences? references)
    {
        if (_disposed || !_editors.TryResolveCellId(binding, out _)) return;
        ClearLanguageResults();
        _referenceResults.Show(references);
        OpenPanelWindow("compilation");
        _referenceResults.Focus();
    }

    private string? ActiveLanguageBinding()
    {
        if (_activeEntity is not { IsValid: true } active) return null;
        foreach (var (group, entity) in _editorEntities) {
            if (entity == active && _editors.GetBindingId(group) is { } binding) return binding;
        }
        return null;
    }

    private void InvalidateLanguageContext()
    {
        _renamePanel.Invalidate();
        _referenceResults.Clear();
        _editors.InvalidateLanguageContext();
    }

    private string? ActiveLanguageGroup(Entity entity)
    {
        var tab = TabIdFor(entity);
        return NotebookCellLayout.EnumerateGroups(_state).FirstOrDefault(group => group.ActiveTabId == tab)?.Id;
    }

    private NavigationBookmark? CaptureNavigationBookmark(string? binding = null)
    {
        if (binding is not null && _editors.TryResolveCellId(binding, out var origin)
            && _editorEntities.TryGetValue(origin, out var entity) && entity.IsValid
            && _editors.TryGetPosition(origin, out var position)) return new(entity, position with { History = null });
        if (_activeEntity is { IsValid: true } active && ActiveLanguageGroup(active) is { } group
            && _editors.TryGetPosition(group, out var current)) return new(active, current with { History = null }, _generatedNavigationTarget);
        return null;
    }

    private static void PushNavigation(List<NavigationBookmark> stack, NavigationBookmark? bookmark)
    {
        if (bookmark is not { } value) return;
        if (stack.Count > 0 && stack[^1].Entity == value.Entity && stack[^1].Generated == value.Generated
            && stack[^1].Position.Selection.Eq(value.Position.Selection)) return;
        stack.Add(value);
        if (stack.Count > 128) stack.RemoveAt(0);
    }

    private void HandleLanguageSourceChanged(string binding)
    {
        if (_disposed) return;
        _renamePanel.Invalidate();
        ClearLanguageResults();
        _referenceResults.Clear();
        ClearDiagnostics();
        SyncDirtyIndicators();
        CheckShaderSources();
        UpdateCursorStatus();
        ScheduleLanguageDiagnostics(binding);
    }

    private void HandleDefinitionReady(string binding, SymbolDefinition? definition)
    {
        if (_disposed || !_editors.TryResolveCellId(binding, out _)) return;
        _referenceResults.Clear();
        if (definition is null || definition.Locations.Count == 0) {
            _workspaceStatus.Text(definition is null ? "No symbol at the cursor" : $"{definition.Display} · external definition");
            return;
        }
        if (definition.Locations.Count == 1) { Track(NavigateToSourceAsync(definition.Locations[0], binding)); return; }
        ClearLanguageResults();
        _definitionTargets = definition.Locations;
        _languageResults.ToggleClass("hidden", false);
        AddLanguageElement(DomElement.Create("strong").Text(definition.Display));
        for (var index = 0; index < definition.Locations.Count; index++) {
            var target = definition.Locations[index];
            AddLanguageElement(DomElement.Create("button").Class("editor-page-diagnostic").Attr("type", "button")
                .On("click", $"editor-language-definition:{_definitionGeneration}:{index}")
                .Text($"{target.Path}:{target.Line}:{target.Column}"));
        }
        OpenPanelWindow("compilation");
    }

    private void AddLanguageElement(DomElement element) { _languageElements.Add(element); _languageResults.Append(element); }

    private void ClearLanguageResults()
    {
        _definitionGeneration++;
        _definitionTargets = [];
        _generatedNavigationTarget = null;
        foreach (var element in _languageElements) { element.Remove(); element.Dispose(); }
        _languageElements.Clear();
        _languageResults.ToggleClass("hidden", true);
    }

    private async Task NavigateToSourceAsync(SourcePosition target, string? originBinding = null)
    {
        var origin = CaptureNavigationBookmark(originBinding);
        if (target.Generated) {
            PushNavigation(_navigationBack, origin);
            _navigationForward.Clear();
            ShowGeneratedDefinition(target);
            return;
        }
        if (!_workspace.TryGetEntity(target.DocumentId, out var entity) || !entity.IsValid) return;
        await OpenEntryAsync(entity, pin: true);
        if (_disposed || _activeEntity != entity || ActiveLanguageGroup(entity) is not { } group) return;
        if (_editors.GetSource(group) != target.Source) { _workspaceStatus.Text("Source changed · invoke navigation again"); return; }
        PushNavigation(_navigationBack, origin);
        _navigationForward.Clear();
        ClearLanguageResults();
        _editors.NavigateToLine(group, target.Line, target.Column, target.Length);
        UpdateCursorStatus();
    }

    private void ShowGeneratedDefinition(SourcePosition target)
    {
        ClearLanguageResults();
        _generatedNavigationTarget = target;
        AddLanguageElement(DomElement.Create("strong").Text($"{target.Path}:{target.Line}:{target.Column} · generated source (read only)"));
        var preview = DomElement.Create("pre").Class("editor-generated-source").Attr("tabindex", "0").Attr("aria-label", "Generated source, read only");
        var before = DomElement.Create("span").Text(target.Source[..target.Start]);
        var selected = DomElement.Create("mark").Text(target.Source.Substring(target.Start, target.Length));
        var after = DomElement.Create("span").Text(target.Source[(target.Start + target.Length)..]);
        _languageElements.AddRange([preview, before, selected, after]);
        _languageResults.Append(preview.Append(before).Append(selected).Append(after));
        _languageResults.ToggleClass("hidden", false);
        OpenPanelWindow("compilation");
        preview.Focus();
        DomRuntime.EnsureVisible(_languageResults, selected);
    }

    private async Task NavigateHistoryAsync(bool forward)
    {
        var source = forward ? _navigationForward : _navigationBack;
        var destination = forward ? _navigationBack : _navigationForward;
        var current = CaptureNavigationBookmark();
        while (source.Count > 0) {
            var target = source[^1];
            source.RemoveAt(source.Count - 1);
            if (!target.Entity.IsValid) continue;
            await OpenEntryAsync(target.Entity, pin: true);
            if (_disposed || _activeEntity != target.Entity || ActiveLanguageGroup(target.Entity) is not { } group) return;
            PushNavigation(destination, current);
            if (target.Generated is { } generated) { ShowGeneratedDefinition(generated); return; }
            ClearLanguageResults();
            _editors.RestorePosition(group, target.Position);
            _editors.Focus(group);
            UpdateCursorStatus();
            return;
        }
    }

    private bool RouteLanguageTools(string eventName, string argument)
    {
        switch (eventName) {
            case "editor-language-back": Track(NavigateHistoryAsync(false)); return true;
            case "editor-language-forward": Track(NavigateHistoryAsync(true)); return true;
            case "editor-language-check": ScheduleLanguageDiagnostics(ActiveLanguageBinding(), immediate: true); return true;
            case "editor-language-diagnostic": Track(NavigateDiagnosticAsync(argument)); return true;
            case "editor-language-reference": return _referenceResults.Route(argument);
            case "editor-navigation-picker": return _navigationPicker.Route(argument);
            case "editor-language-rename": Track(_renamePanel.RouteAsync(argument)); return true;
            case "editor-language-definition":
                var parts = argument.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var generation) && generation == _definitionGeneration
                    && int.TryParse(parts[1], out var index) && index >= 0 && index < _definitionTargets.Count) Track(NavigateToSourceAsync(_definitionTargets[index]));
                return true;
            default: return false;
        }
    }
}
