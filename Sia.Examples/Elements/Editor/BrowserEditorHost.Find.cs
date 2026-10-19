namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private readonly EditorFindPanel _find;

    private bool ExecuteFindCommand(EditorCommandId command, BrowserSelection selection)
    {
        Commit(ApplyNativeSelection(State.Value, selection));
        CancelCompletion(); CancelSignature(); CancelLanguageCommand();
        if (command is EditorCommandId.Find or EditorCommandId.Replace) {
            _find.Open(State.Value, command == EditorCommandId.Replace);
            FindNext(1, includeCurrent: true);
        }
        else {
            if (!_find.IsOpen) _find.Open(State.Value, replace: false);
            FindNext(command == EditorCommandId.PreviousMatch ? -1 : 1);
        }
        return true;
    }

    private bool HandleFind(string argument)
    {
        if (!_find.IsCurrentEvent(argument)) return true;
        SynchronizeNativeInput();
        if (!_find.TryAction(argument, out var action)) return true;
        if (action is "replace" or "all" && !_find.InputsCurrent) { _find.Refresh(State.Value.Doc); _find.ResultsChanged(); return true; }
        switch (action) {
            case "close": _find.Close(); _view.SetFindMatch(false); _view.Focus(); return true;
            case "next": FindNext(1); break;
            case "previous": FindNext(-1); break;
            case "replace": ReplaceMatches(all: false); break;
            case "all": ReplaceMatches(all: true); break;
            case "toggle": break;
            default:
                _find.Refresh(State.Value.Doc);
                FindNext(1, includeCurrent: true);
                break;
        }
        return true;
    }

    private void FindNext(int direction, bool includeCurrent = false)
    {
        var state = State.Value;
        if (!ReferenceEquals(state.Doc, _find.Document)) _find.Refresh(state.Doc);
        var matches = _find.Result.Matches;
        if (matches.Count == 0) { _view.SetFindMatch(false); return; }
        var range = state.Selection.Main;
        var current = _find.Selected;
        if (!includeCurrent && current >= 0 && current < matches.Count
            && IsFindSelection(state, matches[current]))
            current = (current + direction + matches.Count) % matches.Count;
        else {
            current = direction > 0 ? 0 : matches.Count - 1;
            for (var index = 0; index < matches.Count; index++) {
                if (direction > 0 && matches[index].From >= range.From) { current = index; break; }
                if (direction < 0 && matches[index].To <= range.From) current = index;
            }
        }
        _find.Selected = current;
        _view.SetFindMatch(true);
        var match = matches[current];
        Commit(state.Apply(new() { Selection = EditorSelection.Single(match.From, match.To) }));
        _view.RevealSelection();
        _find.ShowSummary();
    }

    private void ReplaceMatches(bool all)
    {
        var state = State.Value;
        if (!ReferenceEquals(state.Doc, _find.Document)) _find.Refresh(state.Doc);
        var result = _find.Result;
        if (result.Error is not null || result.Matches.Count == 0) return;
        var matches = result.Matches;
        if (!all) {
            var index = _find.Selected;
            if (index < 0 || index >= matches.Count || !IsFindSelection(state, matches[index])) { FindNext(1, includeCurrent: true); return; }
            matches = [matches[index]];
        }
        var changes = matches.Select(static match => new ChangeSpec(match.From, match.To, match.Replacement)).ToArray();
        var after = state.Apply(new() { Changes = changes });
        if (!all) {
            var match = matches[0];
            var next = System.Math.Min(after.Doc.Length, match.From + match.Replacement.Length + (match.From == match.To ? 1 : 0));
            after = after.Apply(new() { Selection = EditorSelection.Single(next) });
        }
        Commit(after, historySelection: state.Selection);
        FindNext(1, includeCurrent: true);
    }

    private static bool IsFindSelection(EditorState state, EditorSearchMatch match)
        => state.Selection.Eq(EditorSelection.Single(match.From, match.To).Normalize(state.Doc));
}
