using System.Globalization;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private State<EditorViewport>? _viewport;
    private double _lastClientHeight = 400;
    private double _lastScrollTop;
    private int _scrollGeneration;
    private EditorMemento? _pendingRestore;
    private bool _lineWrapping = true;

    internal void ConfigureLineWrapping(bool enabled)
    {
        if (_lineWrapping == enabled) return;
        SynchronizeNativeInput();
        if (TryGetPosition(out var position)) _pendingRestore = position;
        _lineWrapping = enabled;
        _view.SetLineWrapping(enabled);
    }

    private bool HandleMeasure(string arguments)
    {
        var parts = arguments.Split(':', 10);
        if (parts.Length != 10
            || !TryParseInvariant(parts[0], out var scrollTop)
            || !TryParseInvariant(parts[1], out var clientHeight)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstLineIndex)
            || !TryParseInvariant(parts[3], out var contentWidth)
            || !TryParseInvariant(parts[4], out var charWidth)
            || !TryParseInvariant(parts[5], out var lineHeight)
            || !int.TryParse(parts[7], out var revision)
            || !int.TryParse(parts[8], out var scrollGeneration)
            || !bool.TryParse(parts[9], out var lineWrapping) || lineWrapping != _lineWrapping
            || scrollGeneration > _scrollGeneration || scrollGeneration < -_scrollGeneration
            || revision < 0 || revision > _documentRevision) {
            return false;
        }

        foreach (var obsolete in _domDocuments.Keys.Where(value => value < revision).ToArray()) {
            _domDocuments.Remove(obsolete);
        }
        if (revision != _documentRevision) {
            return false;
        }

        if (scrollGeneration < 0) {
            _pendingRestore = null;
            scrollGeneration = -scrollGeneration;
        }

        double[]? heights = null;
        if (parts[6].Length > 0 && !TryParseHeights(parts[6], out heights)) {
            return false;
        }

        _lastClientHeight = clientHeight;
        var state = State.Value;
        var oracle = _viewState.SetDocument(state.Doc);

        var oracleChanged = false;
        var lineLength = contentWidth / charWidth;
        if (charWidth > 1 && lineHeight > 0 && contentWidth > 20 && lineLength >= 10) {
            oracleChanged = oracle.Refresh(
                lineWrapping: _lineWrapping,
                lineHeight: lineHeight,
                charWidth: charWidth,
                textHeight: lineHeight,
                lineLength: lineLength,
                knownHeights: heights ?? []);
        }

        if (oracleChanged && _pendingRestore is null
            && _view.ReadScrollPosition() is { Top: > 0, Line: >= 0 } position
            && position.Line < state.Doc.Lines) {
            _pendingRestore = new EditorMemento(state.Selection, position.Line, position.Column,
                position.Offset, position.Top, position.Left);
        }

        _view.BeginUpdate();
        try {
            var hasMeasuredLines = heights is { Length: > 0 } && firstLineIndex >= 0 && firstLineIndex < state.Doc.Lines;
            if (hasMeasuredLines || oracleChanged) {
                _viewState.UpdateMeasuredHeights(firstLineIndex, heights, force: oracleChanged);
            }

            if (oracleChanged && _pendingRestore is not null) {
                ++_scrollGeneration;
            }
            if (scrollGeneration != _scrollGeneration) {
                if (_pendingRestore is { } pending) {
                    RestoreViewport(pending);
                    _world.FlushReactive();
                }
                return true;
            }
            _pendingRestore = null;

            var bias = scrollTop - _lastScrollTop;
            _lastScrollTop = scrollTop;
            var viewport = _viewState.ComputeViewport(scrollTop, scrollTop + clientHeight, bias);
            if (_viewState.IsAppropriate(Viewport.Value, bias)) {
                viewport = Viewport.Value;
                _viewState.SetMainViewport(viewport);
            }
            ApplyViewport(viewport);
            _world.FlushReactive();
            return true;
        }
        finally {
            _view.EndUpdate();
        }
    }

    private static bool TryParseInvariant(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryParseHeights(string csv, out double[] heights)
    {
        var segments = csv.Split(',');
        heights = new double[segments.Length];
        for (var index = 0; index < segments.Length; index++) {
            if (!TryParseInvariant(segments[index], out heights[index])) {
                return false;
            }
        }
        return true;
    }

    private void ApplyViewport(EditorViewport viewport)
    {
        if (!viewport.Equals(Viewport.Value)) {
            Viewport.Set(viewport);
        }
        PushSpacerHeights(viewport);
    }

    private void PushSpacerHeights(EditorViewport viewport)
    {
        var map = _viewState.HeightMap;
        var before = viewport.From <= 0
            ? 0
            : _viewState.LineAt(viewport.From, QueryType.ByPos).Top;
        var afterBlock = _viewState.LineAt(viewport.To, QueryType.ByPos);
        var after = System.Math.Max(0, map.Height - afterBlock.Bottom);
        _view.SetSpacerHeights(before, after);
    }

    public bool TryGetPosition(out EditorMemento memento)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = State.Value;
        var history = CaptureHistory();
        if (_pendingRestore is { } pending) {
            memento = pending with { Selection = state.Selection, History = history };
            return true;
        }
        var position = _view.ReadScrollPosition();
        var captured = position ?? new(_lastScrollTop, 0, -1, 0, 0);
        if (captured.Line < 0) {
            var block = _viewState.LineAt(captured.Top, QueryType.ByHeight);
            captured = captured with {
                Line = state.Doc.LineAt(block.From).Number - 1,
                Offset = (block.Top - captured.Top) / _viewState.Oracle.LineHeight,
            };
        }
        memento = new(state.Selection, captured.Line, captured.Column, captured.Offset, captured.Top, captured.Left) { History = history };
        return true;
    }

    public void RestorePosition(EditorMemento memento)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RestoreHistory(memento.History);
        _view.BeginUpdate();
        try {
            var state = State.Value;
            var selection = memento.Selection.Ranges.All(range => range.To <= state.Doc.Length)
                ? memento.Selection
                : EditorSelection.Create([..memento.Selection.Ranges.Select(range => range.Clamp(state.Doc.Length))],
                    memento.Selection.MainIndex);
            ++_scrollGeneration;
            _pendingRestore = memento with { Selection = selection, History = null };
            Commit(state.Apply(new() { Selection = selection }), revealSelection: false);
            RestoreViewport(_pendingRestore.Value);
            _world.FlushReactive();
        }
        finally {
            _view.EndUpdate();
        }
    }

    private void RestoreViewport(EditorMemento memento)
    {
        var doc = State.Value.Doc;
        var line = doc.Line(System.Math.Clamp(memento.ViewLine + 1, 1, doc.Lines));
        var block = _viewState.LineAt(line.From, QueryType.ByPos);
        var column = System.Math.Clamp(memento.ViewColumn, 0, line.Length);
        var rowTop = _lineWrapping
            ? System.Math.Min(block.Height - _viewState.Oracle.LineHeight,
                System.Math.Floor(column / _viewState.Oracle.LineLength) * _viewState.Oracle.LineHeight)
            : 0;
        var top = System.Math.Max(0, block.Top + System.Math.Max(0, rowTop) - memento.ViewOffset * _viewState.Oracle.LineHeight);
        ApplyViewport(_viewState.ComputeViewport(top, top + _lastClientHeight));
        _view.RestoreScroll(_scrollGeneration, new(top, memento.ScrollLeft, line.Number - 1, column, memento.ViewOffset));
    }

    private EditorViewport EnsureSelectionVisible(EditorState state, EditorViewport viewport)
    {
        var headPosition = state.Selection.Main.Head;
        if (!viewport.Contains(headPosition)) {
            viewport = _viewState.EnsureIncludes(
                new EditorScrollTarget(headPosition, ScrollYStrategy.Nearest), _lastClientHeight);
        }

        return viewport;
    }

    private State<EditorViewport> Viewport
        => _viewport ??= _mount.GetState<EditorViewport>(1);
}
