using Sia_Examples.Dom;

namespace Sia_Examples.Editor;

internal sealed class EditorRenamePanel(BrowserEditorRegistry editors, EditorWorkspaceEdits edits, string eventName, Action returnFocus) : IDisposable
{
    private readonly DomElement _root = DomElement.Find("app");
    private readonly List<DomElement> _elements = [];
    private readonly List<DomElement> _previewElements = [];
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private DomElement? _dialog;
    private DomElement _input = null!;
    private DomElement _message = null!;
    private DomElement _results = null!;
    private CancellationTokenSource? _cancellation;
    private Task<RenamePlan?>? _pending;
    private RenamePlan? _plan;
    private string _binding = "";
    private string _source = "";
    private string _name = "";
    private int _position;
    private int _generation;
    private int _request;
    private int _previewGeneration;
    private int _offset;
    private bool _applying;
    private bool _reportOnly;
    private bool _disposed;

    internal void Open(string binding, RenameTarget target, string source, int position)
    {
        if (_disposed) return;
        Close();
        _binding = binding; _source = source; _position = position; _name = target.Name;
        _dialog = Own(DomElement.Create("dialog").Class("editor-dialog").Attr("aria-label", "Rename Symbol")
            .Attr("data-editor-dialog", eventName).Attr("data-editor-dialog-stamp", Stamp));
        var header = Own(DomElement.Create("header").Class("editor-dialog-header"));
        header.Append(Own(DomElement.Create("h2").Text("Rename Symbol")))
            .Append(Own(DomElement.Create("button").Class("btn").Attr("type", "button").Text("Close").On("click", Payload("close"))));
        _input = Own(DomElement.Create("input").Attr("type", "text").Attr("aria-label", "New symbol name")
            .Attr("autocomplete", "off").Attr("spellcheck", "false").Attr("value", target.Name)
            .Attr("data-editor-dialog-focus", "").Attr("data-editor-dialog-select", "").On("input", Payload("input")));
        _message = Own(DomElement.Create("p").Class("editor-dialog-message").Attr("role", "status"));
        _results = Own(DomElement.Create("div").Class("editor-dialog-list").Attr("aria-label", "Rename preview"));
        var footer = Own(DomElement.Create("footer").Class("editor-dialog-footer"));
        footer.Append(Own(DomElement.Create("button").Class("btn").Attr("type", "button").Text("Rename").On("click", Payload("accept"))))
            .Append(Own(DomElement.Create("span").Text("Enter to rename · Escape to cancel")));
        _dialog.Append(header).Append(_input).Append(_message).Append(_results).Append(footer);
        _root.Append(_dialog);
        if (target.Error is { } error) _message.Text(error);
        else _pending = PrepareAsync(debounce: false);
    }

    private string Stamp => $"{_instance}:{_generation}";
    private string Payload(string action) => $"{eventName}:{Stamp}:{action}";
    private DomElement Own(DomElement element) { _elements.Add(element); return element; }
    private DomElement Preview(DomElement element) { _previewElements.Add(element); return element; }

    internal async Task<bool> RouteAsync(string argument)
    {
        var parts = argument.Split(':', 4);
        if (_disposed || _dialog is null || parts.Length < 3 || parts[0] != _instance
            || !int.TryParse(parts[1], out var generation) || generation != _generation) return false;
        switch (parts[2]) {
            case "close" or "escape": Close(); returnFocus(); break;
            case "input":
                if (_name == _input.Value()) break;
                _name = _input.Value(); _pending = PrepareAsync(debounce: true); break;
            case "accept":
                if (_reportOnly) { Close(); returnFocus(); break; }
                if (_applying) break;
                if (_name != _input.Value()) { _name = _input.Value(); _pending = PrepareAsync(debounce: false); }
                var pending = _plan is { } ready ? Task.FromResult<RenamePlan?>(ready) : _pending ??= PrepareAsync(debounce: false);
                var request = _request;
                var plan = await pending;
                if (_disposed || _dialog is null || generation != _generation || request != _request || _input.Value() != _name || plan?.CanApply != true) break;
                _applying = true;
                try {
                    if (edits.Apply(plan)) { Close(); returnFocus(); }
                    else { _plan = null; _pending = null; }
                }
                finally { _applying = false; }
                break;
            case "previous" or "next" when parts.Length == 4 && int.TryParse(parts[3], out var preview) && preview == _previewGeneration && _plan is not null:
                _offset = Math.Max(0, _offset + (parts[2] == "next" ? 50 : -50)); RenderPreview(); break;
            default: return false;
        }
        return true;
    }

    internal void Invalidate()
    {
        if (_dialog is null || _applying) return;
        _request++; _cancellation?.Cancel(); _plan = null; _pending = null;
        ClearPreview(); _message.Text("The workspace changed. Press Enter to check the rename again.");
    }

    internal void Report(string message)
    {
        if (_disposed) return;
        if (_dialog is not null) { _message.Text(message); return; }
        Close(); _reportOnly = true;
        _dialog = Own(DomElement.Create("dialog").Class("editor-dialog").Attr("aria-label", "Workspace Edit")
            .Attr("data-editor-dialog", eventName).Attr("data-editor-dialog-stamp", Stamp));
        var header = Own(DomElement.Create("header").Class("editor-dialog-header"));
        header.Append(Own(DomElement.Create("h2").Text("Workspace Edit")))
            .Append(Own(DomElement.Create("button").Class("btn").Attr("type", "button").Attr("data-editor-dialog-focus", "")
                .Text("Close").On("click", Payload("close"))));
        _message = Own(DomElement.Create("p").Class("editor-dialog-message").Attr("role", "alert").Text(message));
        _root.Append(_dialog.Append(header).Append(_message));
    }

    private async Task<RenamePlan?> PrepareAsync(bool debounce)
    {
        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = _generation;
        var request = ++_request;
        var name = _name;
        _plan = null; ClearPreview(); _message.Text("Checking rename…");
        try {
            if (debounce) await Task.Delay(250, cancellation.Token);
            var plan = await editors.PrepareRenameAsync(_binding, _source, _position, name, cancellation.Token);
            if (_disposed || _dialog is null || generation != _generation || request != _request || cancellation.IsCancellationRequested) return null;
            _plan = plan; _offset = 0; RenderPreview();
            return plan;
        }
        catch (OperationCanceledException) {
            if (!cancellation.IsCancellationRequested && _dialog is not null && generation == _generation && request == _request)
                _message.Text("The source changed. Close this panel and rename from the current symbol.");
            return null;
        }
        catch (Exception error) {
            if (_dialog is not null && generation == _generation && request == _request) _message.Text(error.Message);
            return null;
        }
        finally {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    private void ClearPreview()
    {
        _previewGeneration++;
        _results?.Text("");
        foreach (var element in _previewElements) element.Dispose();
        _previewElements.Clear();
    }

    private void RenderPreview()
    {
        ClearPreview();
        if (_plan is not { } plan) return;
        if (plan.Conflicts.Count > 0) {
            _message.Text($"{plan.Conflicts.Count} conflicts · no changes will be applied");
            foreach (var conflict in plan.Conflicts.Take(50))
                _results.Append(Preview(DomElement.Create("p").Class("editor-dialog-row").Text(conflict.Message)));
            return;
        }
        var changes = plan.Documents.SelectMany(document => document.Changes.Select(change => (Document: document, Change: change))).ToArray();
        _offset = Math.Min(_offset, Math.Max(0, (changes.Length - 1) / 50 * 50));
        _message.Text(changes.Length == 0 ? "Enter a new name." : $"{changes.Length} changes in {plan.Documents.Count} files");
        foreach (var (document, change) in changes.Skip(_offset).Take(50)) {
            var line = 1 + document.Before.AsSpan(0, change.From).Count('\n');
            var row = Preview(DomElement.Create("div").Class("editor-dialog-row"));
            row.Append(Preview(DomElement.Create("span").Text($"{document.Path}:{line}")));
            var code = Preview(DomElement.Create("code"));
            code.Append(Preview(DomElement.Create("del").Text(document.Before[change.From..change.To!.Value])))
                .Append(Preview(DomElement.Create("span").Text(" → ")))
                .Append(Preview(DomElement.Create("ins").Text(change.Insert ?? "")));
            _results.Append(row.Append(code));
        }
        if (changes.Length > 50) {
            var footer = Preview(DomElement.Create("div").Class("editor-dialog-footer"));
            foreach (var direction in new[] { "previous", "next" }) {
                var enabled = direction == "previous" ? _offset > 0 : _offset + 50 < changes.Length;
                if (enabled) footer.Append(Preview(DomElement.Create("button").Class("btn").Attr("type", "button").Text(direction == "previous" ? "Previous" : "Next")
                    .On("click", Payload(direction) + ":" + _previewGeneration)));
            }
            footer.Append(Preview(DomElement.Create("span").Text($"{_offset + 1}–{Math.Min(_offset + 50, changes.Length)} / {changes.Length}")));
            _results.Append(footer);
        }
    }

    private void Close()
    {
        _generation++; _request++; _cancellation?.Cancel(); _plan = null; _pending = null;
        ClearPreview(); _dialog?.Remove();
        foreach (var element in _elements) element.Dispose();
        _elements.Clear(); _dialog = null; _results = null!; _reportOnly = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Close(); _root.Dispose();
    }
}
