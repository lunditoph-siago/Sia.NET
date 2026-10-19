using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private ProgramConsole? _programConsole;
    private bool _runningMain;
    private int _consoleRevision = -1;

    private void SetProgramConsole(ProgramConsole console)
    {
        _programConsole?.Dispose();
        _programConsole = console;
        _consoleRevision = -1;
        RefreshProgramConsole();
    }

    private void RefreshProgramConsole()
    {
        if (_programConsole is not { } console) return;
        var snapshot = console.Snapshot(_consoleRevision);
        if (snapshot.Revision == _consoleRevision) return;
        _consoleRevision = snapshot.Revision;
        _programOutput.Text(snapshot.Text).ToggleClass("output-error", snapshot.Error.Length != 0);
    }

    private async Task PumpProgramConsoleAsync()
    {
        while (!_disposed) {
            await Task.Delay(100, _lifetime.Token);
            if (_disposed) return;
            RefreshProgramConsole();
            DomRuntime.Flush();
        }
    }

    private async Task<bool> TryRunMainAsync(ShaderCompilation source, int revision, CancellationToken token)
    {
        if (source.Program.EntryPoint is null) return false;
        token.ThrowIfCancellationRequested();
        StopPipeline(cancelPending: false);
        _shaderPreview?.Dispose();
        _shaderPreview = null;
        _shaderPreviewSnapshot = null;
        var console = new ProgramConsole();
        SetProgramConsole(console);
        _runningMain = true;
        UpdateActions();
        SetBuildStatus("Main running · Console output is independent of rendering");
        OpenPanelWindow("console");
        DomRuntime.Flush();
        try {
            var execution = Task.Run(() => ManagedAssemblyExecutor.ExecuteAsync(source.Program, console));
            var result = await execution.WaitAsync(_lifetime.Token);
            if (!_disposed && revision == _shaderRevision) {
                RefreshProgramConsole();
                SetBuildStatus(result.Success ? "Program completed" : "Program failed · see Console");
            }
        }
        finally { _runningMain = false; console.Dispose(); }
        return true;
    }
}
