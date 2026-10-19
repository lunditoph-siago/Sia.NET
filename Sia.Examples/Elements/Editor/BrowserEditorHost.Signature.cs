using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private SignatureHelp? _signatureHelp;
    private DomElement? _signaturePopup;
    private readonly List<DomElement> _signatureElements = [];
    private CancellationTokenSource? _signatureCancellation;
    private bool _signatureRunning;
    private bool _signatureRequested;
    private int _signatureGeneration;
    private int _signatureIndex;
    private int _signatureCallStart = -1;
    private string? _signatureChoice;
    private string SignatureScheduleKey => $"signature:{_cellId}";

    private void ScheduleSignature(EditorState state, bool manual = false)
    {
        var position = state.Selection.Main.Head;
        var trigger = position > 0 && state.Doc.SliceDoc(position - 1, position)[0] is '(' or ',' or '[';
        if (!manual && !_signatureRequested && !trigger) return;
        CancelSignature(keepSession: true);
        if (!state.Selection.Main.Empty) { CancelSignature(); return; }
        _signatureRequested = true;
        _signatureCancellation = new();
        DomRuntime.ScheduleEvent(SignatureScheduleKey, $"signature:{_cellId}:{_signatureGeneration}", manual ? 0 : 100);
        PlaceSignature();
    }

    private bool HandleSignatureRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation) || generation != _signatureGeneration
            || _signatureCancellation is not { IsCancellationRequested: false } cancellation || _signatureRunning) return false;
        _signatureRunning = true;
        _ = QuerySignatureAsync(State.Value, cancellation, generation);
        return true;
    }

    private async Task QuerySignatureAsync(EditorState state, CancellationTokenSource cancellation, int generation)
    {
        try {
            var result = await _completionProvider.QuerySignatureAsync(_cellId, state.Doc.SliceDoc(), state.Selection.Main.Head, cancellation.Token);
            if (generation != _signatureGeneration || cancellation.IsCancellationRequested
                || !ReferenceEquals(state.Doc, State.Value.Doc) || !state.Selection.Eq(State.Value.Selection)) return;
            if (result is null) { CancelSignature(); return; }
            if (_signatureCallStart != result.Start) _signatureChoice = null;
            _signatureCallStart = result.Start;
            _signatureHelp = result;
            _signatureIndex = result.Selected;
            if (_signatureChoice is { } choice) {
                for (var index = 0; index < result.Items.Count; index++) {
                    if (result.Items[index].Applicable && SignatureKey(result.Items[index]) == choice) { _signatureIndex = index; break; }
                }
            }
            RenderSignature();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) {
            if (generation == _signatureGeneration && !cancellation.IsCancellationRequested) {
                CancelSignature();
                DomRuntime.ReportError(error.ToString());
            }
        }
        finally {
            if (ReferenceEquals(_signatureCancellation, cancellation)) {
                _signatureCancellation = null;
                _signatureRunning = false;
            }
            cancellation.Dispose();
        }
    }

    private bool HandleSignatureStep(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var generation) || generation != _signatureGeneration
            || !int.TryParse(parts[1], out var step) || step is not (-1 or 1)) return false;
        StepSignature(step);
        _view.Focus();
        return true;
    }

    private void StepSignature(int step)
    {
        if (_signatureHelp is not { } help) return;
        _signatureIndex = (_signatureIndex + step + help.Items.Count) % help.Items.Count;
        _signatureChoice = SignatureKey(help.Items[_signatureIndex]);
        RenderSignature();
    }

    private static string SignatureKey(SignatureItem item) => item.Prefix + string.Join(",", item.Parameters) + item.Suffix;

    private void RenderSignature()
    {
        if (_signatureHelp is not { } help) return;
        _view.BeginUpdate();
        try {
            var popup = _signaturePopup ??= DomElement.Create("section").Class("signature-help")
                .Id($"{_cellId}-parameters").Attr("role", "status").Attr("aria-live", "polite")
                .Attr("aria-label", "C# parameter hints").Attr("data-overlay-side", "above");
            popup.Text("");
            foreach (var element in _signatureElements) element.Dispose();
            _signatureElements.Clear();
            DomElement Own(DomElement element) { _signatureElements.Add(element); return element; }
            var item = help.Items[_signatureIndex];
            var toolbar = Own(DomElement.Create("div").Class("signature-toolbar"));
            toolbar.Append(Own(DomElement.Create("span").Text($"{_signatureIndex + 1} / {help.Items.Count}")));
            if (help.Items.Count > 1) {
                foreach (var (step, label, text) in new[] { (-1, "Previous overload", "‹"), (1, "Next overload", "›") }) {
                    toolbar.Append(Own(DomElement.Create("button").Attr("type", "button").Attr("aria-label", label)
                        .On("click", $"signature-step:{_cellId}:{_signatureGeneration}:{step}").Text(text)));
                }
            }
            popup.Append(toolbar);
            var signature = Own(DomElement.Create("div").Class("signature-label"));
            signature.ToggleClass("inapplicable", !item.Applicable);
            signature.Append(Own(DomElement.Create("span").Text(item.Prefix)));
            for (var index = 0; index < item.Parameters.Length; index++) {
                if (index != 0) signature.Append(Own(DomElement.Create("span").Text(", ")));
                signature.Append(Own(DomElement.Create(index == item.ActiveParameter ? "strong" : "span").Text(item.Parameters[index])));
            }
            signature.Append(Own(DomElement.Create("span").Text(item.Suffix)));
            popup.Append(signature);
            if (item.ParameterDocumentation.Length > 0)
                popup.Append(Own(DomElement.Create("p").Class("signature-parameter-doc").Text(item.ParameterDocumentation)));
            if (item.Documentation.Length > 0) popup.Append(Own(DomElement.Create("p").Text(item.Documentation)));
            _view.SetSignatureHelp($"{_cellId}-parameters");
            PlaceSignature();
        }
        finally { _view.EndUpdate(); }
    }

    private void PlaceSignature()
    {
        if (_signaturePopup is not { } popup || _disposed) return;
        if (_completionPopup is { } completion) {
            DomRuntime.ClearOverlayPlacement(popup);
            popup.Attr("style", "");
            popup.ToggleClass("combined", true);
            completion.InsertBefore(popup, _completionList);
            if (_completion is { } result) RenderCompletionPlacement(result);
            RevealCompletionSelection();
        }
        else {
            popup.ToggleClass("combined", false);
            popup.Attr("data-overlay-side", "above");
            var position = State.Value.Selection.Main.Head;
            var line = State.Value.Doc.LineAt(position);
            _view.MountOverlay(popup, line.Number - 1, position - line.From);
        }
    }

    private void CancelSignature(bool keepSession = false)
    {
        _signatureGeneration++;
        DomRuntime.CancelScheduledEvent(SignatureScheduleKey);
        var cancellation = _signatureCancellation;
        var running = _signatureRunning;
        _signatureCancellation = null;
        _signatureRunning = false;
        cancellation?.Cancel();
        if (!running) cancellation?.Dispose();
        if (!keepSession) {
            _signatureHelp = null;
            _view.SetSignatureHelp("");
            if (_signaturePopup is { } popup) {
                DomRuntime.ClearOverlayPlacement(popup);
                popup.Remove();
                popup.Dispose();
                _signaturePopup = null;
            }
            foreach (var element in _signatureElements) element.Dispose();
            _signatureElements.Clear();
            _signatureRequested = false;
            _signatureChoice = null;
            _signatureCallStart = -1;
        }
    }
}
