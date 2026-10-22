namespace Sia.Examples.Notebook;

public static partial class NotebookCellLayout
{
    internal static NotebookCellState SynchronizeSurfaces(
        NotebookCellState state,
        NotebookSessionSnapshot previousSnapshot,
        NotebookSessionSnapshot snapshot)
    {
        var previous = previousSnapshot.Cells.ToDictionary(static cell => cell.Id, static cell => cell.State);
        var changes = new Dictionary<string, SurfaceChanges>(StringComparer.Ordinal);
        foreach (var cell in snapshot.Cells) {
            var presentationId = state.GetScriptWindow(cell.Id).CellId;
            changes.TryGetValue(presentationId, out var change);
            changes[presentationId] = change.Observe(
                cell.State, previous.TryGetValue(cell.Id, out var old) ? old : null);
        }
        foreach (var (presentationId, change) in changes) {
            state = change.Apply(state, presentationId);
        }
        return state;
    }

    private readonly record struct SurfaceChanges(
        bool HasOutput, bool HadOutput, bool OutputChanged,
        bool Rendering, bool WasRendering, bool RenderChanged)
    {
        public SurfaceChanges Observe(CellState current, CellState? previous)
        {
            var completedRun = previous?.Phase == CellPhase.Running
                && current.Phase is CellPhase.RanSuccess or CellPhase.RanError;
            return new(
                HasOutput: HasOutput || HasText(current),
                HadOutput: HadOutput || previous is { } oldOutput && HasText(oldOutput),
                OutputChanged: OutputChanged || previous is not { } old
                    || old.StandardOutput != current.StandardOutput
                    || old.StandardError != current.StandardError || completedRun,
                Rendering: Rendering || current.RenderRequested,
                WasRendering: WasRendering || previous?.RenderRequested == true,
                RenderChanged: RenderChanged || current.RenderRequested && (previous is not { } oldRender
                    || !oldRender.RenderRequested
                    || oldRender.RenderOutput != current.RenderOutput
                    || completedRun));
        }

        public NotebookCellState Apply(NotebookCellState state, string cellId)
        {
            state = UpdateWindow(state, cellId, CellWindowKind.Output,
                open: HasOutput && (!HadOutput || OutputChanged), close: !HasOutput && HadOutput);
            return UpdateWindow(state, cellId, CellWindowKind.Render,
                open: Rendering && RenderChanged, close: !Rendering && WasRendering);
        }

        private static bool HasText(CellState state)
            => state.StandardOutput.Length > 0 || state.StandardError.Length > 0;

        private static NotebookCellState UpdateWindow(
            NotebookCellState state, string cellId, CellWindowKind kind, bool open, bool close)
        {
            if (!open && !close) return state;
            // Output and Render window keys use the presentation cell's ID.
            var windowId = NotebookCellState.WindowId(cellId, kind);
            return open ? OpenWindow(state, windowId) : CloseWindow(state, windowId);
        }
    }
}
