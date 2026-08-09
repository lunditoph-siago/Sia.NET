using System.Collections.Immutable;
using Sia;
using Sia.Reactive;
using Sia_Examples;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class ReactiveCompositionAcceptance : IAcceptanceStage
{
    public string Name => "2. Reactive composition";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync(
            "editor reconciles only the changed keyed line",
            TestEditorLineReconciliationAsync);
        await context.CaseAsync(
            "editor selection bypasses the text-line branch",
            TestEditorSelectionReconciliationAsync);
        await context.CaseAsync(
            "200 selections keep a 1,000-line text branch cold",
            TestEditorSelectionPressureAsync);
        await context.CaseAsync(
            "500 edits keep 999 unrelated lines cold",
            TestEditorEditPressureAsync);
        await context.CaseAsync(
            "notebook reconciles cells, packages, and count independently",
            TestNotebookReconciliationAsync);
        await context.CaseAsync(
            "example selector reconciles only changed active items",
            TestExampleReconciliationAsync);
    }

    private static Task TestEditorLineReconciliationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var view = new RecordingEditorView();
        var initial = EditorState.Create("one\ntwo\nthree", DecorationSet.Empty);
        var mount = world.Mount(EditorViewComponent.Definition, new(view, initial));
        world.FlushReactive();
        AcceptanceAssert.Equal(3, view.LineUpserts.Count);

        view.Clear();
        var state = mount.GetState<EditorState>();
        state.Set(state.Value.Apply(new() { Changes = [new(4, 7, "TWO")] }));
        world.FlushReactive();

        AcceptanceAssert.Equal(1, view.LineUpserts.Count);
        AcceptanceAssert.Equal(1, view.LineUpserts[0].Identity);
        AcceptanceAssert.Equal(0, view.LineRemovals.Count);

        view.Clear();
        var editedLine = state.Value.Doc.Line(2);
        state.Set(state.Value.Apply(new() {
            Changes = [new(editedLine.From + 1, editedLine.From + 1, "\n")],
        }));
        world.FlushReactive();
        AcceptanceAssert.SequenceEqual(
            [1, 2, 3],
            view.LineUpserts.Select(static line => line.Index).Order());
        AcceptanceAssert.Equal(0, view.LineRemovals.Count);

        view.Clear();
        var splitLine = state.Value.Doc.Line(2);
        state.Set(state.Value.Apply(new() {
            Changes = [new(splitLine.To, splitLine.To + 1, string.Empty)],
        }));
        world.FlushReactive();
        AcceptanceAssert.SequenceEqual(
            [1, 2],
            view.LineUpserts.Select(static line => line.Index).Order());
        AcceptanceAssert.SequenceEqual(
            [3],
            view.LineRemovals.Select(static line => line.Identity));
        mount.Unmount();
        return Task.CompletedTask;
    }

    private static Task TestEditorSelectionReconciliationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var view = new RecordingEditorView();
        var initial = EditorState.Create("one\ntwo\nthree", DecorationSet.Empty);
        var mount = world.Mount(EditorViewComponent.Definition, new(view, initial));
        world.FlushReactive();

        view.Clear();
        var state = mount.GetState<EditorState>();
        state.Set(state.Value.Apply(new() {
            Selection = EditorSelection.Single(state.Value.Doc.Length),
        }));
        world.FlushReactive();

        AcceptanceAssert.Equal(0, view.LineUpserts.Count);
        AcceptanceAssert.Equal(0, view.ActiveLineRemovals.Count);
        AcceptanceAssert.SequenceEqual(
            [2],
            view.ActiveLineUpserts.Select(static line => line.Identity));
        AcceptanceAssert.Equal(1, view.SelectionUpserts);
        AcceptanceAssert.Equal(1, view.StatusUpserts);
        mount.Unmount();
        return Task.CompletedTask;
    }

    private static Task TestNotebookReconciliationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var view = new RecordingNotebookView();
        var cells = ImmutableArray.Create(
            new NotebookCellSnapshot("a", CellState.Create("A")),
            new NotebookCellSnapshot("b", CellState.Create("B")));
        var snapshot = new NotebookSessionSnapshot(1, cells, []);
        var mount = world.Mount(NotebookViewComponent.Definition, new(view, snapshot));
        world.FlushReactive();
        AcceptanceAssert.Equal(2, view.CellUpserts.Count);
        AcceptanceAssert.SequenceEqual([0], view.PackageCounts);

        view.Clear();
        cells = cells.SetItem(1, cells[1] with {
            State = cells[1].State with { Phase = CellPhase.Compiling },
        });
        snapshot = new(2, cells, []);
        mount.Update(new(view, snapshot));
        world.FlushReactive();
        AcceptanceAssert.Equal(1, view.CellUpserts.Count);
        AcceptanceAssert.Equal("b", view.CellUpserts[0].Id);
        AcceptanceAssert.Equal(0, view.PackageUpserts.Count);
        AcceptanceAssert.Equal(0, view.PackageCounts.Count);

        view.Clear();
        var package = new PackageRef(PackageSource.Framework, "System.Runtime", null);
        snapshot = new(
            3,
            cells,
            [new(package, PackageLoadState.Loading, null)]);
        mount.Update(new(view, snapshot));
        world.FlushReactive();
        AcceptanceAssert.Equal(0, view.CellUpserts.Count);
        AcceptanceAssert.Equal(1, view.PackageUpserts.Count);
        AcceptanceAssert.SequenceEqual([1], view.PackageCounts);

        view.Clear();
        snapshot = new(
            4,
            cells,
            [new(package, PackageLoadState.Loaded, null)]);
        mount.Update(new(view, snapshot));
        world.FlushReactive();
        AcceptanceAssert.Equal(1, view.PackageUpserts.Count);
        AcceptanceAssert.Equal(0, view.PackageCounts.Count);
        mount.Unmount();
        return Task.CompletedTask;
    }

    private static Task TestEditorSelectionPressureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var view = new RecordingEditorView();
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 1_000).Select(static index => $"line{index:D4}"));
        var initial = EditorState.Create(source, DecorationSet.Empty);
        var mount = world.Mount(EditorViewComponent.Definition, new(view, initial));
        world.FlushReactive();

        view.Clear();
        var state = mount.GetState<EditorState>();
        for (var iteration = 1; iteration <= 200; iteration++) {
            var line = state.Value.Doc.Line(iteration * 17 % 1_000 + 1);
            state.Set(state.Value.Apply(new() {
                Selection = EditorSelection.Single(line.From),
            }));
            world.FlushReactive();
        }

        AcceptanceAssert.Equal(0, view.LineUpserts.Count);
        AcceptanceAssert.Equal(0, view.LineRemovals.Count);
        AcceptanceAssert.Equal(200, view.ActiveLineUpserts.Count);
        AcceptanceAssert.Equal(200, view.SelectionUpserts);
        AcceptanceAssert.Equal(200, view.StatusUpserts);
        mount.Unmount();
        return Task.CompletedTask;
    }

    private static Task TestEditorEditPressureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var view = new RecordingEditorView();
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 1_000).Select(static index => $"line{index:D4}"));
        var initial = EditorState.Create(source, DecorationSet.Empty);
        var activeLine = initial.Doc.Line(501);
        initial = initial.Apply(new() {
            Selection = EditorSelection.Single(activeLine.To),
        });
        var mount = world.Mount(EditorViewComponent.Definition, new(view, initial));
        world.FlushReactive();

        view.Clear();
        var state = mount.GetState<EditorState>();
        for (var iteration = 0; iteration < 500; iteration++) {
            var head = state.Value.Selection.Main.Head;
            state.Set(state.Value.Apply(new() {
                Changes = [new(head, head, "x")],
                Selection = EditorSelection.Single(head + 1),
            }));
            world.FlushReactive();
        }

        AcceptanceAssert.Equal(500, view.LineUpserts.Count);
        AcceptanceAssert.True(
            view.LineUpserts.All(static line => line.Identity == 500),
            "An unrelated editor line was reconciled.");
        AcceptanceAssert.Equal(0, view.LineRemovals.Count);
        AcceptanceAssert.Equal(0, view.ActiveLineUpserts.Count);
        AcceptanceAssert.Equal(500, view.SelectionUpserts);
        AcceptanceAssert.Equal(500, view.StatusUpserts);
        mount.Unmount();
        return Task.CompletedTask;
    }

    private static Task TestExampleReconciliationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var world = new World();
        var host = new RecordingExampleHost();
        var library = new NotebookLibrary();
        var mount = world.Mount(ExampleApp.Definition, new(library, host));
        world.FlushReactive();
        AcceptanceAssert.Equal(library.Notebooks.Count, host.Upserts.Count);

        var state = mount.GetState<ExampleAppState>();
        state.Set(new(0));
        world.FlushReactive();
        host.Clear();

        state.Set(new(2));
        world.FlushReactive();
        AcceptanceAssert.Equal(2, host.Upserts.Count);
        AcceptanceAssert.SequenceEqual(
            [0, 2],
            host.Upserts.Select(static item => item.Index).Order());
        mount.Unmount();
        return Task.CompletedTask;
    }
}
