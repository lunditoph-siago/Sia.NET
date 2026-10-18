#if BROWSER
using Sia_Examples.Dom;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_Examples.Browser;

internal sealed partial class BrowserDomBackend
{
    public IEditorLinesOwner CreateEditorLines(
        DomElement gutter, DomElement lines, DomElement gutterBefore,
        DomElement gutterAfter, DomElement linesBefore, DomElement linesAfter)
        => new NativeEditorLines(this, gutter, lines, gutterBefore, gutterAfter, linesBefore, linesAfter);

    private sealed class NativeEditorLines(
        BrowserDomBackend backend, DomElement gutter, DomElement lines,
        DomElement gutterBefore, DomElement gutterAfter, DomElement linesBefore,
        DomElement linesAfter) : IEditorLinesOwner
    {
        private EditorLinesView _view;
        private int? _active;
        private bool _disposed;

        public bool Apply(in EditorLinesView view, IReadOnlySet<int> identities)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_view.Lines.Span.SequenceEqual(view.Lines.Span)) return false;
            WriteRows(view, false);
            _view = view;
            return true;
        }

        public void Restore()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            WriteRows(_view, true);
        }

        private void WriteRows(in EditorLinesView view, bool force)
        {
            backend.VerifyAccess();
            foreach (var element in new[] { gutter, lines, gutterBefore, gutterAfter, linesBefore, linesAfter }) backend.Owned(element.Node);
            backend.Write(DomWriteKind.EditorLines, backend.Index(lines.Node), backend.Index(gutter.Node), view.Lines.Length);
            backend._writes.Add(backend.Index(gutterBefore.Node));
            backend._writes.Add(backend.Index(gutterAfter.Node));
            backend._writes.Add(backend.Index(linesBefore.Node));
            backend._writes.Add(backend.Index(linesAfter.Node));
            backend._writes.Add(_active.HasValue ? 1 : 0);
            backend._writes.Add(_active.GetValueOrDefault());
            backend._writes.Add(force ? 1 : 0);
            foreach (var row in view.Lines.Span) {
                backend._writes.Add(row.Identity);
                backend._writes.Add(row.Index);
                backend._writes.Add(row.ClassName is null ? -1 : backend.StringIndex(row.ClassName));
                backend._writes.Add(row.StyledRuns is null ? backend.StringIndex(row.Text) : -1);
                backend._writes.Add(row.StyledRuns?.Count ?? -1);
                if (row.StyledRuns is { } runs) {
                    foreach (var run in runs) {
                        backend._writes.Add(backend.StringIndex(run.Text));
                        backend._writes.Add(backend.StringIndex(run.Class is { } classification ? CSharpHighlighter.CssClass(classification) : string.Empty));
                    }
                }
            }
            if (backend._updates.Count == 0) backend.FlushPending();
        }

        public void SetActiveLine(int? identity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active == identity) return;
            backend.VerifyAccess();
            backend.Write(DomWriteKind.ActiveEditorLine, backend.Index(lines.Node), identity.GetValueOrDefault(), identity.HasValue ? 1 : 0);
            if (backend._updates.Count == 0) backend.FlushPending();
            _active = identity;
        }

        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            backend.VerifyAccess();
            backend.Write(DomWriteKind.ClearEditorLines, backend.Index(lines.Node));
            if (backend._updates.Count == 0) backend.FlushPending();
            _view = default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            backend.VerifyAccess();
            backend.Write(DomWriteKind.DisposeEditorLines, backend.Index(lines.Node));
            if (backend._updates.Count == 0) backend.FlushPending();
            _view = default;
            _active = null;
            _disposed = true;
        }
    }
}
#endif
