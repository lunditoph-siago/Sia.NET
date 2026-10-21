using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly DomElement _diagnosticStatus;
    private readonly DomElement _diagnostics;

    private void RenderDiagnostics(IReadOnlyList<CSharpCompilationDiagnostic> diagnostics)
    {
        var errors = diagnostics.Count(static diagnostic => diagnostic.Severity == NotebookDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(static diagnostic => diagnostic.Severity == NotebookDiagnosticSeverity.Warning);
        _diagnosticStatus.Text($"{errors} errors · {warnings} warnings")
            .Attr("aria-label", $"{errors} errors, {warnings} warnings. Open compiler diagnostics")
            .Attr("title", "Diagnostics for the compiled C# snapshot")
            .ToggleClass("is-empty", errors == 0 && warnings == 0)
            .ToggleClass("has-errors", errors != 0);
        DomRuntime.BeginUpdate(_diagnostics);
        try {
            ClearDiagnostics();
            var generation = _diagnosticsGeneration;
            foreach (var diagnostic in diagnostics) {
                var location = diagnostic.SourcePath is null
                    ? string.Empty
                    : $"{diagnostic.SourcePath}({diagnostic.Line},{diagnostic.Column}): ";
                var text = $"{location}{diagnostic.Severity.ToString().ToLowerInvariant()} "
                    + $"{diagnostic.Id}: {diagnostic.Message}";
                using var line = DomElement.Create(diagnostic.SourceId is null ? "div" : "button")
                    .Class("editor-page-diagnostic")
                    .Class(diagnostic.Severity == NotebookDiagnosticSeverity.Error
                        ? "diagnostic-error"
                        : "diagnostic-warning")
                    .Text(text);
                if (diagnostic.SourceId is { } fileId) {
                    line.Attr("type", "button").On("click", $"editor-language-diagnostic:{generation}:{Uri.EscapeDataString(fileId)}:{diagnostic.Line}:{diagnostic.Column}");
                }
                _diagnostics.Append(line);
            }
        }
        finally {
            DomRuntime.EndUpdate(_diagnostics);
        }
    }

    private void ResetDiagnosticStatus()
        => _diagnosticStatus.Text("Not checked").Attr("aria-label", "Not checked. Open compiler diagnostics")
            .Attr("title", "Compile to check the current source")
            .ToggleClass("is-empty", true).ToggleClass("has-errors", false);
    private int _diagnosticsGeneration;
    private int _liveDiagnosticsGeneration;
    private CancellationTokenSource? _liveDiagnosticsCancellation;

    private void ScheduleLanguageDiagnostics(string? binding, bool immediate = false)
    {
        var generation = ++_liveDiagnosticsGeneration;
        _liveDiagnosticsCancellation?.Cancel();
        if (_disposed || binding is null) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _liveDiagnosticsCancellation = cancellation;
        Track(CheckLanguageDiagnosticsAsync(binding, generation, cancellation, immediate));
    }

    private void ClearDiagnostics() { _diagnosticsGeneration++; _diagnostics.Text(string.Empty); }

    private void CancelLanguageDiagnostics()
    {
        _liveDiagnosticsGeneration++;
        _liveDiagnosticsCancellation?.Cancel();
        _liveDiagnosticsCancellation = null;
    }

    private async Task CheckLanguageDiagnosticsAsync(string binding, int generation, CancellationTokenSource cancellation, bool immediate)
    {
        try {
            if (!immediate) await Task.Delay(750, cancellation.Token);
            if (!_editors.TryResolveCellId(binding, out _)) return;
            var issues = await _editors.QueryDiagnosticsAsync(binding, cancellation.Token, sourceBoundary: immediate);
            if (_disposed || cancellation.IsCancellationRequested || generation != _liveDiagnosticsGeneration) return;
            var diagnostics = issues.Select(issue => {
                string? key = null;
                if (issue.Location is { Generated: false } target && _workspace.TryGetEntity(target.DocumentId, out var file) && file.IsValid) key = KeyFor(file);
                return new CSharpCompilationDiagnostic(issue.Id, issue.Message, issue.Error ? NotebookDiagnosticSeverity.Error : NotebookDiagnosticSeverity.Warning,
                    key, issue.Location?.Path, issue.Location?.Line ?? 0, issue.Location?.Column ?? 0);
            }).ToArray();
            RenderDiagnostics(diagnostics);
            _diagnosticStatus.Attr("title", "Editor diagnostics for the current source snapshot");
            if (immediate) OpenPanelWindow("compilation");
        }
        catch (OperationCanceledException) when (_disposed || cancellation.IsCancellationRequested
            || generation != _liveDiagnosticsGeneration || !_editors.TryResolveCellId(binding, out var group)
            || !_editorEntities.TryGetValue(group, out var entity) || !entity.IsValid) { }
        catch (Exception error) { if (!_disposed && !cancellation.IsCancellationRequested && generation == _liveDiagnosticsGeneration) ShowOperationError("Could not check the current code", error); }
        finally {
            if (ReferenceEquals(_liveDiagnosticsCancellation, cancellation)) _liveDiagnosticsCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task NavigateDiagnosticAsync(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 4 || !int.TryParse(parts[0], out var generation) || generation != _diagnosticsGeneration
            || !TryResolve(Uri.UnescapeDataString(parts[1]), out var entity) || !entity.IsValid
            || !int.TryParse(parts[2], out var line) || !int.TryParse(parts[3], out var column)) return;
        var origin = CaptureNavigationBookmark();
        await OpenEntryAsync(entity, pin: true);
        if (_disposed || generation != _diagnosticsGeneration || _activeEntity != entity || ActiveLanguageGroup(entity) is not { } group) return;
        PushNavigation(_navigationBack, origin);
        _navigationForward.Clear();
        _editors.NavigateToLine(group, line, column);
        UpdateCursorStatus();
    }
}
