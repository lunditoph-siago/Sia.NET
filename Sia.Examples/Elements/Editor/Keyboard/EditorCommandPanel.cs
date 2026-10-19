using System.Text.Json;
using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

internal sealed class EditorCommandPanel : IDisposable
{
    private readonly DomElement _root = DomElement.Find("app");
    private readonly DomElement _chordStatus = DomElement.Create("span").Id("keymap-chord").Class("keymap-chord").Attr("role", "status");
    private readonly List<DomElement> _elements = [];
    private readonly List<DomElement> _rows = [];
    private readonly List<DomElement> _formElements = [];
    private EditorKeymap _keymap = EditorKeymap.Default;
    private DomElement? _dialog;
    private DomElement? _search;
    private DomElement? _list;
    private DomElement? _message;
    private DomElement? _form;
    private DomElement? _shortcut;
    private DomElement? _context;
    private EditorCommand[] _matches = [];
    private Func<EditorCommand, bool> _available = static _ => false;
    private string _binding = "";
    private string? _loadError;
    private bool _settings;
    private bool _recording;
    private int _selected;
    private int _generation;
    private int _profileVersion;
    private int _recordingVersion;
    private (EditorCommand Command, int Index)? _editing;

    internal EditorCommandPanel()
    {
        _root.Append(_chordStatus);
        try { if (DomRuntime.Backend.ReadKeymap() is { } json) _keymap = EditorKeymap.Deserialize(json); }
        catch (Exception error) { _loadError = $"Keyboard settings could not be loaded: {error.Message}"; }
        Project();
    }

    private DomElement Own(DomElement element, bool form = false) { (form ? _formElements : _elements).Add(element); return element; }

    private DomElement Button(string text, string action, string argument = "", bool form = false)
        => Own(DomElement.Create("button").Attr("type", "button").Text(text)
            .On("click", $"keymap-{action}:{_generation}:{argument}"), form);

    internal void Open(bool settings, string binding, Func<EditorCommand, bool> available)
    {
        Close();
        _settings = settings;
        _binding = binding;
        _available = available;
        _dialog = Own(DomElement.Create("dialog").Class("keymap-dialog")
            .Attr("data-keymap-dialog", settings ? "settings" : "commands")
            .Attr("data-keymap-generation", _generation.ToString())
            .Attr("aria-label", settings ? "Keyboard Shortcuts" : "Command Palette"));
        DomRuntime.BeginUpdate(_dialog);
        try {
            var header = Own(DomElement.Create("header").Class("keymap-header"));
            header.Append(Own(DomElement.Create("h2").Text(settings ? "Keyboard Shortcuts" : "Command Palette")))
                .Append(Button("Close", "close").Class("btn"));
            _search = Own(DomElement.Create("input").Attr("type", "search")
                .Attr("aria-label", settings ? "Search keyboard shortcuts" : "Search commands")
                .Attr("placeholder", settings ? "Search commands or shortcuts" : "Type a command")
                .Attr("autocomplete", "off").Attr("data-keymap-search", "")
                .On("input", $"keymap-filter:{_generation}:"));
            if (!settings) _search.Attr("role", "combobox").Attr("aria-autocomplete", "list")
                .Attr("aria-expanded", "true").Attr("aria-controls", "keymap-command-list");
            _list = Own(DomElement.Create("div").Class("keymap-list").Id("keymap-command-list")
                .Attr("role", settings ? "list" : "listbox").Attr("aria-label", settings ? "Command bindings" : "Commands"));
            _message = Own(DomElement.Create("p").Class("keymap-message").Attr("role", "status"));
            _dialog.Append(header).Append(_search).Append(_list).Append(_message);
            if (settings) {
                var footer = Own(DomElement.Create("footer").Class("keymap-footer"));
                footer.Append(Own(DomElement.Create("span").Text("Mod means Ctrl on Windows/Linux and ⌘ on macOS. Two-step chords use a space.")))
                    .Append(Button("Reset all…", "reset-all").Class("btn"));
                _dialog.Append(footer);
            }
            Render();
            if (_loadError is { } error) _message.Text(error);
            _root.Append(_dialog);
        }
        finally { DomRuntime.EndUpdate(_dialog); }
    }

    internal bool Route(string eventName, string argument, out EditorCommand? invocation, out string binding)
    {
        invocation = null;
        binding = _binding;
        if (!eventName.StartsWith("keymap-", StringComparison.Ordinal)) return false;
        var separator = argument.IndexOf(':');
        var stamp = separator < 0 ? argument : argument[..separator];
        if (_dialog is null || !int.TryParse(stamp, out var generation) || generation != _generation) return true;
        var value = separator < 0 ? "" : argument[(separator + 1)..];
        try {
            switch (eventName) {
                case "keymap-close": Close(); break;
                case "keymap-escape":
                    if (_editing is not null) EndEdit(); else Close();
                    break;
                case "keymap-filter": Render(); break;
                case "keymap-move" when !_settings && int.TryParse(value, out var step) && _matches.Length > 0:
                    _selected = System.Math.Clamp(_selected + step, 0, _matches.Length - 1);
                    SelectRow();
                    break;
                case "keymap-accept" when !_settings && _matches.Length > 0:
                    invocation = _matches[_selected];
                    if (_available(invocation)) Close(); else invocation = null;
                    break;
                case "keymap-run" when !_settings:
                    invocation = _matches.FirstOrDefault(command => command.Id.ToString() == value && _available(command));
                    if (invocation is not null) Close();
                    break;
                case "keymap-edit" when _settings: {
                        var parts = value.Split(':', 3);
                        if (parts.Length == 3 && parts[2] == _profileVersion.ToString() && EditorCommand.Find(parts[0]) is { } command && int.TryParse(parts[1], out var index))
                            BeginEdit(command, index);
                        break;
                    }
                case "keymap-remove" when _settings: {
                        var parts = value.Split(':', 3);
                        if (parts.Length == 3 && parts[2] == _profileVersion.ToString() && EditorCommand.Find(parts[0]) is { } command && int.TryParse(parts[1], out var index)) {
                            var bindings = _keymap.Bindings(command);
                            if (index >= 0 && index < bindings.Length) Save(_keymap.Set(command, [.. bindings.Where((_, i) => i != index)]));
                        }
                        break;
                    }
                case "keymap-reset" when _settings:
                    var resetParts = value.Split(':', 2);
                    if (resetParts.Length == 2 && resetParts[1] == _profileVersion.ToString()
                        && EditorCommand.Find(resetParts[0]) is { } reset) Save(_keymap.Set(reset, null));
                    break;
                case "keymap-reset-all" when _settings: {
                        _message!.Text("Reset every custom keyboard shortcut?");
                        using var confirm = DomElement.Create("button").Attr("type", "button").Class("btn").Text("Reset all")
                            .On("click", $"keymap-confirm-reset:{_generation}:");
                        _message.Append(confirm);
                        break;
                    }
                case "keymap-confirm-reset" when _settings: Save(EditorKeymap.Default); break;
                case "keymap-cancel-edit": EndEdit(); break;
                case "keymap-record" when _shortcut is not null:
                    _recording = !_recording;
                    _shortcut.Attr("data-keymap-recording", _recording ? "true" : "false");
                    if (_recording) _shortcut.Attr("data-keymap-record-id", (++_recordingVersion).ToString());
                    _shortcut.Focus();
                    _message!.Text(_recording ? "Press one or two key combinations, then choose Apply. Escape stops recording." : "Recording stopped.");
                    break;
                case "keymap-apply" when _editing is { } edit:
                    _recording = false;
                    _shortcut?.Attr("data-keymap-recording", "false");
                    var existing = _keymap.Bindings(edit.Command);
                    if (_shortcut is null || _context is null || !Enum.TryParse<CommandContext>(_context.Value(), out var context)) break;
                    var replacement = new KeyBinding(_shortcut.Value(), context);
                    var updated = edit.Index == existing.Length ? [.. existing, replacement] : existing.ToArray();
                    if (edit.Index < 0 || edit.Index > existing.Length) break;
                    updated[edit.Index] = replacement;
                    Save(_keymap.Set(edit.Command, updated));
                    break;
            }
        }
        catch (Exception error) { _message?.Text(error.Message); }
        return true;
    }

    private void Save(EditorKeymap updated)
    {
        DomRuntime.Backend.WriteKeymap(updated.Serialize());
        _keymap = updated;
        _profileVersion++;
        _loadError = null;
        EndEdit();
        Project();
        Render();
        _message!.Text("Keyboard shortcuts saved.");
    }

    private void Project() => _root.Attr("data-editor-keymap", JsonSerializer.Serialize(_keymap.Compile(), JsonSerializerOptions.Web));

    private void Render()
    {
        if (_list is null || _search is null) return;
        var terms = _search.Value().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DomRuntime.BeginUpdate(_list);
        try {
            foreach (var row in _rows) { row.Remove(); row.Dispose(); }
            _rows.Clear();
            _matches = [.. EditorCommand.All.Where(command => terms.All(term =>
            command.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || command.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
            || _keymap.Bindings(command).Any(binding => binding.Key.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(command => _settings || _available(command)).ThenBy(static command => command.Title, StringComparer.OrdinalIgnoreCase)];
            _selected = 0;
            for (var index = 0; index < _matches.Length; index++) {
                var command = _matches[index];
                var bindings = _keymap.Bindings(command);
                var row = DomElement.Create(_settings ? "div" : "button").Class("keymap-row");
                _rows.Add(row);
                _list.Append(row);
                using var label = DomElement.Create("span").Class("keymap-command-title").Text(command.Title);
                row.Append(label);
                if (!_settings) {
                    row.Attr("type", "button").Attr("role", "option").Id($"keymap-option-{index}")
                        .Attr("aria-disabled", _available(command) ? "false" : "true")
                        .On("click", $"keymap-run:{_generation}:{command.Id}");
                    using var keys = DomElement.Create("kbd").Text(bindings.FirstOrDefault()?.Key ?? "");
                    row.Append(keys);
                    continue;
                }
                row.Attr("role", "listitem");
                using var actions = DomElement.Create("div").Class("keymap-bindings");
                for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++) {
                    var binding = bindings[bindingIndex];
                    actions.Append(RowButton(binding.Key, "edit", $"{command.Id}:{bindingIndex}")
                        .Attr("title", $"Edit {command.Title} in {binding.When ?? command.Context}"))
                        .Append(RowButton("×", "remove", $"{command.Id}:{bindingIndex}")
                            .Attr("aria-label", $"Remove {binding.Key} from {command.Title}"));
                }
                if (bindings.Length == 0) actions.Append(RowButton("Unbound", "edit", $"{command.Id}:0"));
                actions.Append(RowButton("+", "edit", $"{command.Id}:{bindings.Length}").Attr("aria-label", $"Add shortcut for {command.Title}"));
                if (_keymap.Overrides.ContainsKey(command.Id.ToString()))
                    actions.Append(RowButton("Reset", "reset", command.Id.ToString()).Attr("aria-label", $"Reset {command.Title}"));
                row.Append(actions);
            }
            if (_matches.Length == 0) {
                var empty = DomElement.Create("p").Class("keymap-empty").Text("No matching commands.");
                _rows.Add(empty); _list.Append(empty);
            }
            if (!_settings) SelectRow();
            else _message?.Text($"{_matches.Length} commands · select a shortcut to edit it");
        }
        finally { DomRuntime.EndUpdate(_list); }
    }

    private DomElement RowButton(string text, string action, string argument)
    {
        var button = DomElement.Create("button").Attr("type", "button").Text(text)
            .On("click", $"keymap-{action}:{_generation}:{argument}:{_profileVersion}");
        _rows.Add(button);
        return button;
    }

    private void SelectRow()
    {
        for (var index = 0; index < _matches.Length; index++)
            _rows[index].Attr("aria-selected", index == _selected ? "true" : "false");
        _search!.Attr("aria-activedescendant", _matches.Length > 0 ? $"keymap-option-{_selected}" : "");
        if (_matches.Length > 0) DomRuntime.EnsureVisible(_list!, _rows[_selected]);
    }

    private void BeginEdit(EditorCommand command, int index)
    {
        EndEdit();
        var bindings = _keymap.Bindings(command);
        if (index < 0 || index > bindings.Length) return;
        _editing = (command, index);
        var binding = index < bindings.Length ? bindings[index] : new KeyBinding("", command.Context);
        _form = Own(DomElement.Create("section").Class("keymap-edit").Attr("aria-label", "Edit shortcut"), form: true);
        _form.Append(Own(DomElement.Create("h3").Text(command.Title), form: true));
        _shortcut = Own(DomElement.Create("input").Attr("type", "text").Attr("aria-label", "Shortcut")
            .Attr("value", binding.Key).Attr("placeholder", "Mod+Shift+B or Mod+K Mod+S")
            .Attr("autocomplete", "off").Attr("spellcheck", "false").Attr("data-keymap-shortcut", ""), form: true);
        _context = Own(DomElement.Create("select").Attr("aria-label", "When"), form: true);
        foreach (var context in Enum.GetValues<CommandContext>().Where(context => EditorKeymap.Allows(command.Context, context))) {
            var option = Own(DomElement.Create("option").Attr("value", context.ToString()).Text(context.ToString()), form: true);
            if (context == (binding.When ?? command.Context)) option.Attr("selected", "");
            _context.Append(option);
        }
        _form.Append(_shortcut).Append(_context).Append(Button("Record keys", "record", form: true).Class("btn"))
            .Append(Button("Apply", "apply", form: true).Class("btn")).Append(Button("Cancel", "cancel-edit", form: true).Class("btn"));
        _dialog!.InsertBefore(_form, _message);
        _dialog.ToggleClass("is-editing", true);
        _shortcut.Focus();
        _message!.Text("Only this command changes. An empty binding list disables its shortcuts.");
    }

    private void EndEdit()
    {
        _form?.Remove();
        foreach (var element in _formElements) element.Dispose();
        _formElements.Clear();
        _form = null;
        _editing = null;
        _shortcut = null;
        _context = null;
        _recording = false;
        _dialog?.ToggleClass("is-editing", false);
        if (_settings) _message?.Text($"{_matches.Length} commands · select a shortcut to edit it");
        _search?.Focus();
    }

    internal void Close()
    {
        _generation++;
        _dialog?.Remove();
        foreach (var element in _formElements) element.Dispose();
        _formElements.Clear();
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
        foreach (var element in _elements) element.Dispose();
        _elements.Clear();
        _dialog = _search = _list = _message = _form = _shortcut = _context = null;
        _editing = null;
        _matches = [];
    }

    public void Dispose() { Close(); _chordStatus.Remove(); _chordStatus.Dispose(); _root.Attr("data-editor-keymap", ""); _root.Dispose(); }
}
