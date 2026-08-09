#if !BROWSER
using Sia_Examples.Console;

namespace Sia_BrowserTest.Acceptance;

public sealed class ConsoleDomAcceptance : IAcceptanceStage
{
    public string Name => "6. Console DOM polyfill";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync(
            "DOM mutations render the shared application structure",
            TestRenderingAsync);
        await context.CaseAsync(
            "focused Console elements dispatch DOM listener payloads",
            TestInteractionAsync);
        await context.CaseAsync(
            "sidebar and notebook render as side-by-side panes",
            TestSideBySideLayoutAsync);
        await context.CaseAsync(
            "Tab switches the active pane's focus target",
            TestPaneSwitchAsync);
        await context.CaseAsync(
            "extreme terminal sizes render without throwing",
            TestExtremeSizesAsync);
        await context.CaseAsync(
            "a real cell's header collapses into one line",
            TestCellHeaderCollapsesAsync);
        await context.CaseAsync(
            "a real cell's controls collapse into one line",
            TestCellControlsCollapseAsync);
        await context.CaseAsync(
            "an editable cell renders one code line per row, blank lines included",
            TestEditableCellLinesAsync);
        await context.CaseAsync(
            "an editable cell's gutter and status bar never leak into the code",
            TestEditableCellSkipsChromeAsync);
        await context.CaseAsync(
            "diagnostics render one entry per row",
            TestDiagnosticsOnePerRowAsync);
    }

    private static Task TestCellHeaderCollapsesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        BuildCellHeader(backend);

        backend.Flush();

        var row = terminal.Rows.FirstOrDefault(r => r.Contains("cell-a"));
        AcceptanceAssert.True(row is not null, "expected the cell label to render");
        AcceptanceAssert.Contains("idle", row!, "expected the phase badge on the same row as the label");
        return Task.CompletedTask;
    }

    private static Task TestCellControlsCollapseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        BuildCellHeader(backend);

        backend.Flush();

        var row = terminal.Rows.FirstOrDefault(r => r.Contains("Compile"));
        AcceptanceAssert.True(row is not null, "expected the Compile button to render");
        AcceptanceAssert.Contains("Run", row!, "expected Run on the same row as Compile");
        AcceptanceAssert.Contains("Stop", row!, "expected Stop on the same row as Compile");
        return Task.CompletedTask;
    }

    private static Task TestEditableCellLinesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        BuildEditableCode(backend, ["var x = 1;", "", "Console.WriteLine(x);"]);

        backend.Flush();

        var firstIndex = IndexOfRow(terminal.Rows, "var x = 1;");
        var secondIndex = IndexOfRow(terminal.Rows, "Console.WriteLine(x);");
        AcceptanceAssert.True(firstIndex >= 0, "expected the first code line to render on its own row");
        AcceptanceAssert.True(secondIndex >= 0, "expected the second code line to render on its own row");
        AcceptanceAssert.Equal(2, secondIndex - firstIndex, "expected exactly one blank row between the two code lines");
        return Task.CompletedTask;
    }

    private static Task TestEditableCellSkipsChromeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        BuildEditableCode(backend, ["var x = 1;", "Console.WriteLine(x);"]);

        backend.Flush();

        var codeRow = terminal.Rows.First(r => r.Contains("var x = 1;"));
        AcceptanceAssert.False(
            codeRow.Contains("34 chars"),
            "the editor status bar must not leak onto a code line");
        AcceptanceAssert.False(
            terminal.Rows.Any(r => r.Contains("34 chars")),
            "the editor status bar must not render at all in the console view");
        return Task.CompletedTask;
    }

    private static Task TestDiagnosticsOnePerRowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        var notebook = backend.Find("notebook");
        var diagnostics = backend.Create("div");
        backend.ToggleClass(diagnostics, "diagnostics", true);
        foreach (var text in new[] { "(1,5) CS0103: bad", "(2,1) CS0246: worse" }) {
            var diag = backend.Create("div");
            backend.ToggleClass(diag, "diagnostic-error", true);
            backend.SetText(diag, text);
            backend.InsertBefore(diagnostics, diag, before: null);
        }
        backend.InsertBefore(notebook, diagnostics, before: null);

        backend.Flush();

        var firstRow = terminal.Rows.FirstOrDefault(r => r.Contains("CS0103"));
        AcceptanceAssert.True(firstRow is not null, "expected the first diagnostic to render");
        AcceptanceAssert.False(
            firstRow!.Contains("CS0246"),
            "two diagnostics must not be squished onto the same row");
        return Task.CompletedTask;
    }

    private static void BuildCellHeader(ConsoleDomBackend backend)
    {
        var notebook = backend.Find("notebook");
        var header = backend.Create("div");
        backend.ToggleClass(header, "cell-header", true);
        var label = backend.Create("span");
        backend.ToggleClass(label, "cell-label", true);
        backend.SetText(label, "[1] cell-a");
        var phase = backend.Create("span");
        backend.ToggleClass(phase, "phase", true);
        backend.SetText(phase, "idle");
        backend.InsertBefore(header, label, before: null);
        backend.InsertBefore(header, phase, before: null);
        var controls = backend.Create("div");
        backend.ToggleClass(controls, "cell-controls", true);
        foreach (var text in new[] { "Compile", "Run", "Stop" }) {
            var button = backend.Create("button");
            backend.ToggleClass(button, "btn", true);
            backend.SetText(button, text);
            backend.InsertBefore(controls, button, before: null);
        }
        backend.InsertBefore(header, controls, before: null);
        backend.InsertBefore(notebook, header, before: null);
    }

    private static void BuildEditableCode(ConsoleDomBackend backend, string[] lineTexts)
    {
        var notebook = backend.Find("notebook");
        var editor = backend.Create("div");
        backend.ToggleClass(editor, "code", true);
        backend.ToggleClass(editor, "code-edit", true);
        backend.ToggleClass(editor, "editor-container", true);
        var gutter = backend.Create("div");
        backend.ToggleClass(gutter, "editor-gutter", true);
        var scroll = backend.Create("div");
        backend.ToggleClass(scroll, "editor-scroll", true);
        var lines = backend.Create("div");
        backend.ToggleClass(lines, "editor-lines", true);
        foreach (var text in lineTexts) {
            var line = backend.Create("div");
            backend.ToggleClass(line, "editor-line", true);
            backend.SetText(line, text);
            backend.InsertBefore(lines, line, before: null);
        }
        backend.InsertBefore(scroll, lines, before: null);
        var status = backend.Create("div");
        backend.ToggleClass(status, "editor-status", true);
        var docSize = backend.Create("span");
        backend.SetText(docSize, "34 chars");
        backend.InsertBefore(status, docSize, before: null);
        backend.InsertBefore(editor, gutter, before: null);
        backend.InsertBefore(editor, scroll, before: null);
        backend.InsertBefore(editor, status, before: null);
        backend.InsertBefore(notebook, editor, before: null);
    }

    private static int IndexOfRow(IReadOnlyList<string> rows, string needle)
    {
        for (var i = 0; i < rows.Count; i++) {
            if (rows[i].Contains(needle, StringComparison.Ordinal)) {
                return i;
            }
        }
        return -1;
    }

    private static Task TestRenderingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        var sidebar = backend.Find("sidebar");
        var button = backend.Create("button");
        backend.SetText(button, "Open notebook");
        backend.Listen(button, "click", "select:0");
        backend.InsertBefore(sidebar, button, before: null);

        backend.Flush();

        var output = string.Join('\n', terminal.Rows);
        AcceptanceAssert.Contains("Sia.NET Examples", output);
        AcceptanceAssert.Contains("Examples", output);
        AcceptanceAssert.Contains("Open notebook", output);
        return Task.CompletedTask;
    }

    private static async Task TestInteractionAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        terminal.Enqueue(new('\r', ConsoleKey.Enter, false, false, false));
        using var backend = new ConsoleDomBackend(terminal);
        var sidebar = backend.Find("sidebar");
        var button = backend.Create("button");
        backend.SetText(button, "Open notebook");
        backend.Listen(button, "click", "select:0");
        backend.InsertBefore(sidebar, button, before: null);

        var payload = await backend.WaitForEventAsync(cancellationToken);

        AcceptanceAssert.Equal("select:0", payload);
    }

    private static Task TestSideBySideLayoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terminal = new RecordingConsoleTerminal();
        using var backend = new ConsoleDomBackend(terminal);
        var button = backend.Create("button");
        backend.SetText(button, "SidebarMarker");
        backend.InsertBefore(backend.Find("sidebar"), button, before: null);

        backend.Flush();

        var markerRow = terminal.Rows.FirstOrDefault(row => row.Contains("SidebarMarker"));
        AcceptanceAssert.True(markerRow is not null, "expected the sidebar marker to render");
        AcceptanceAssert.Contains("│", markerRow!, "expected a pane divider on the same row as sidebar content");
        AcceptanceAssert.True(
            terminal.Rows.Any(row => row.Contains('┬')) && terminal.Rows.Any(row => row.Contains('┴')),
            "expected a box border with top/bottom pane junctions");
        return Task.CompletedTask;
    }

    private static async Task TestPaneSwitchAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        terminal.Enqueue(new('\t', ConsoleKey.Tab, false, false, false));
        terminal.Enqueue(new('\r', ConsoleKey.Enter, false, false, false));
        using var backend = new ConsoleDomBackend(terminal);

        var sidebarButton = backend.Create("button");
        backend.SetText(sidebarButton, "Sidebar action");
        backend.Listen(sidebarButton, "click", "sidebar-payload");
        backend.InsertBefore(backend.Find("sidebar"), sidebarButton, before: null);

        var notebookButton = backend.Create("button");
        backend.SetText(notebookButton, "Notebook action");
        backend.Listen(notebookButton, "click", "notebook-payload");
        backend.InsertBefore(backend.Find("notebook"), notebookButton, before: null);

        var payload = await backend.WaitForEventAsync(cancellationToken);

        AcceptanceAssert.Equal("notebook-payload", payload);
    }

    private static Task TestExtremeSizesAsync(CancellationToken cancellationToken)
    {
        foreach (var (width, height) in new (int Width, int Height)[] {
            (40, 12), (10, 5), (1, 1), (0, 0), (22, 3),
        }) {
            cancellationToken.ThrowIfCancellationRequested();
            var terminal = new RecordingConsoleTerminal(width, height);
            using var backend = new ConsoleDomBackend(terminal);
            var button = backend.Create("button");
            backend.SetText(button, "X");
            backend.Listen(button, "click", "x");
            backend.InsertBefore(backend.Find("sidebar"), button, before: null);

            try {
                backend.Flush();
            }
            catch (Exception e) {
                throw new AcceptanceException($"{width}x{height} threw during Flush: {e}");
            }
            AcceptanceAssert.Equal(height, terminal.Rows.Count, $"{width}x{height}: row count mismatch");
        }
        return Task.CompletedTask;
    }

    private sealed class RecordingConsoleTerminal(int width = 80, int height = 16) : IConsoleTerminal
    {
        private readonly Queue<ConsoleKeyInfo> _keys = [];

        public int Width => width;

        public int Height => height;

        public IReadOnlyList<string> Rows { get; private set; } = [];

        public void Enqueue(ConsoleKeyInfo key) => _keys.Enqueue(key);

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_keys.Dequeue());
        }

        public void Draw(IReadOnlyList<string> rows) => Rows = [.. rows];

        public void Dispose()
        {
        }
    }
}
#endif
