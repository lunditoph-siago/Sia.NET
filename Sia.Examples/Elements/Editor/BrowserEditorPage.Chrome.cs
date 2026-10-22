using Sia.Examples.Dom;
using Sia.Examples.Notebook;
using Sia;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly DomElement _activeFileName;
    private readonly DomElement _workspaceStatus;
    private readonly DomElement _cursorStatus;
    private readonly DomElement _inputModeStatus;
    private readonly DomElement _lastActionStatus;
    private readonly DomElement _buildStatus;
    private DomElement _compilationDetails = null!;
    private DomElement _compilationDetailsToggle = null!;
    private bool _showCompilationDetails;

    private void UpdateChrome(string? message = null)
    {
        _root.Attr("data-editor-active-binding", ActiveLanguageBinding() ?? string.Empty);
        if (_activeEntity is { } entity && entity.IsValid) {
            UpdateFileStatus(entity, EditorWorkspace.IsDirty(entity));
        }
        else {
            _activeFileName.Text(string.Empty);
        }
        _workspaceStatus.Text(message ?? string.Empty);
        UpdateInputMode(_editors.InputMode);
        UpdateCursorStatus();
    }

    private void UpdateInputMode(EditorInputMode mode)
        => _inputModeStatus.Text(mode.ToString().ToUpperInvariant())
            .ToggleClass("hidden", mode == EditorInputMode.Standard);

    private void UpdateFileStatus(Entity entity, bool dirty)
        => _activeFileName.Text($"{EditorWorkspace.PathOf(entity)}{(dirty ? " ●" : string.Empty)}")
            .Attr("title", $"{EditorWorkspace.PathOf(entity)} · {(entity.Contains<UntitledFile>() ? "Unsaved file" : dirty ? "Unsaved changes" : "Saved")}");

    private void UpdateCursorStatus()
    {
        if (_activeEntity is { IsValid: true } entity) {
            var tabId = TabIdFor(entity);
            foreach (var group in NotebookCellLayout.EnumerateGroups(_state)) {
                if (group.ActiveTabId != tabId || !_editors.TryGetDocumentStatus(group.Id, out var cursor)) continue;
                var label = $"Ln {cursor.Line}, Col {cursor.Column}" +
                    (cursor.Selected == 0 ? string.Empty : $" · {cursor.Selected} selected");
                _cursorStatus.Text(label).Attr("title", $"{label} · {cursor.Lines} lines · {cursor.Length} UTF-16 units");
                return;
            }
        }
        _cursorStatus.Text(string.Empty).Attr("title", string.Empty);
    }

    private DomElement SetBuildStatus(string message) => _buildStatus.Text(message).Attr("title", message);

    private DomElement CreateMenu(DomElement parent, string label, (string Label, string Payload, string Title)[] items)
    {
        var host = Own(DomElement.Create("details").Class("menu-toggle"));
        var summary = Own(DomElement.Create("summary").Class("btn").Attr("aria-label", label).Text(label));
        var menu = Own(DomElement.Create("div").Class("menu-popover").Class("editor-page-menu"));
        foreach (var (text, payload, title) in items) {
            menu.Append(CreateAction(text, payload, title).Class("menu-item"));
        }
        parent.Append(host.Append(summary).Append(menu));
        return menu;
    }

    private DomElement CreateAction(string label, string payload, string title)
    {
        var action = Own(DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("title", title)
            .Text(label));
        return payload.StartsWith("workbench-command:", StringComparison.Ordinal)
            ? action.Attr("data-workbench-command", payload["workbench-command:".Length..])
            : action.On("click", payload);
    }

    private DomElement CreateIconAction(string label, string payload)
    {
        var button = Own(BrowserControls.IconButton(string.Empty, label).Class("editor-action").On("click", payload));
        var icons = label == "Run"
            ? new[] { ("run", "M8 4.5 20 12 8 19.5Z"), ("stop", "M6.5 6.5h11v11h-11Z") }
            : new[] {
                ("compile", "m10.5 4.5 2.5-2.5 9 9-2.5 2.5-3.5-3.5-9.5 9.5a2.12 2.12 0 0 1-3-3l9.5-9.5ZM5.5 14.5l3 3"),
                ("cancel", "m6 6 12 12M6 18 18 6"),
            };
        foreach (var (name, path) in icons) {
            var svg = Own(DomElement.Create("svg").Class("editor-action-icon")
                .Attr("data-icon", name).Attr("viewBox", "0 0 24 24")
                .Attr("width", "20").Attr("height", "20").Attr("fill", "none")
                .Attr("stroke", "currentColor").Attr("stroke-width", "1.4")
                .Attr("stroke-linecap", "round").Attr("stroke-linejoin", "round")
                .Attr("aria-hidden", "true").Attr("focusable", "false"));
            svg.Append(Own(DomElement.Create("path").Attr("d", path)));
            button.Append(svg);
        }
        return button;
    }

    private void ShowCompilationDetails(bool show)
    {
        _showCompilationDetails = show;
        _compilationDetails.ToggleClass("hidden", !show);
        _compilationDetailsToggle.Attr("aria-expanded", show ? "true" : "false");
    }

    private void ShowOperationError(string message, Exception? error = null)
    {
        if (_disposed) return;
        _operationError.Text(message);
        SetBuildStatus(message);
        _compilationOutput.Text(error?.ToString() ?? message).ToggleClass("output-error", true);
        ShowCompilationDetails(true);
        OpenPanelWindow("compilation");
    }
}
