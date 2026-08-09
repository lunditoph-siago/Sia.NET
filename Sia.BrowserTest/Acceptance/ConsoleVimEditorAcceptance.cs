#if !BROWSER
using Sia;
using Sia_Examples.Console;
using Sia_Examples.Dom;
using Sia_Examples.Editor;

namespace Sia_BrowserTest.Acceptance;

public sealed class ConsoleVimEditorAcceptance : IAcceptanceStage
{
    public string Name => "8. Console Vim editor";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync(
            "Enter on an editor surface switches the status bar to NORMAL mode",
            TestEnterActivatesNormalModeAsync);
        await context.CaseAsync(
            "i switches to INSERT mode and typed characters reach the document",
            TestInsertModeTypesTextAsync);
        await context.CaseAsync(
            "x in NORMAL mode deletes the character under the cursor",
            TestNormalModeDeleteCharAsync);
        await context.CaseAsync(
            "Esc from NORMAL mode exits editing back to pane navigation",
            TestEscapeExitsEditorAsync);
        await context.CaseAsync(
            "hjkl motion moves the rendered cursor to the correct line and column",
            TestCursorMotionRendersAtCorrectPositionAsync);
        await context.CaseAsync(
            "o opens a new line below and enters INSERT mode there",
            TestOpenLineBelowAsync);
        await context.CaseAsync(
            "dd deletes the current line entirely",
            TestDeleteLineAsync);
        await context.CaseAsync(
            "gg and G jump to the document start and end",
            TestJumpToDocumentBoundsAsync);
        await context.CaseAsync(
            "O opens a new line above and enters INSERT mode there",
            TestOpenLineAboveAsync);
    }

    private static async Task TestOpenLineAboveAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", "two", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('O'));
            terminal.Enqueue(Char('o'));
            terminal.Enqueue(Char('n'));
            terminal.Enqueue(Char('e'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveUntilQuietAsync(backend, registry, cancellationToken);

            AcceptanceAssert.Equal("one\ntwo", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestOpenLineBelowAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", "one", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('o'));
            terminal.Enqueue(Char('t'));
            terminal.Enqueue(Char('w'));
            terminal.Enqueue(Char('o'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveUntilQuietAsync(backend, registry, cancellationToken);

            AcceptanceAssert.Equal("one\ntwo", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestDeleteLineAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", "one\ntwo\nthree", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('j'));
            terminal.Enqueue(Char('d'));
            terminal.Enqueue(Char('d'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveUntilQuietAsync(backend, registry, cancellationToken);

            AcceptanceAssert.Equal("one\nthree", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestJumpToDocumentBoundsAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", "one\ntwo\nthree", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('G'));
            terminal.Enqueue(Char('x'));
            terminal.Enqueue(Char('g'));
            terminal.Enqueue(Char('g'));
            terminal.Enqueue(Char('x'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveUntilQuietAsync(backend, registry, cancellationToken);

            AcceptanceAssert.Equal("ne\ntwo\nhree", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task DriveUntilQuietAsync(
        ConsoleDomBackend backend,
        BrowserEditorRegistry registry,
        CancellationToken cancellationToken)
    {
        using var driveTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try {
            while (true) {
                var payload = await backend.WaitForEventAsync(driveTimeout.Token);
                registry.Route(payload);
                backend.Flush();
            }
        }
        catch (OperationCanceledException) {
        }
    }

    private static async Task TestCursorMotionRendersAtCorrectPositionAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            registry.Add(container, "cell-1", "var x = 1;\nvar y = 2;", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('l'));
            terminal.Enqueue(Char('l'));
            terminal.Enqueue(Char('j'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            using var driveTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try {
                while (true) {
                    var payload = await backend.WaitForEventAsync(driveTimeout.Token);
                    registry.Route(payload);
                    backend.Flush();
                }
            }
            catch (OperationCanceledException) {
            }

            var cursorRow = terminal.Rows.FirstOrDefault(
                row => row.Contains("va") && row.Contains("y = 2;"));
            AcceptanceAssert.True(
                cursorRow is not null,
                "expected the second code line to render with the cursor split around 'r'");
            AcceptanceAssert.Contains("[7m", cursorRow!, "expected a reverse-video cursor cell");
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestEnterActivatesNormalModeAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            registry.Add(container, "cell-1", "abc", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            using var driveTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            try {
                await backend.WaitForEventAsync(driveTimeout.Token);
            }
            catch (OperationCanceledException) {
            }

            AcceptanceAssert.True(
                terminal.Rows.Any(row => row.Contains("NORMAL", StringComparison.Ordinal)),
                "expected the status bar to show NORMAL mode after activating the editor");
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestInsertModeTypesTextAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", string.Empty, []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('i'));
            terminal.Enqueue(Char('h'));
            terminal.Enqueue(Char('i'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveEventsAsync(backend, registry, cancellationToken, count: 2);

            AcceptanceAssert.Equal("hi", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestNormalModeDeleteCharAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            var host = registry.Add(container, "cell-1", "abc", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Char('x'));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            await DriveEventsAsync(backend, registry, cancellationToken, count: 1);

            AcceptanceAssert.Equal("bc", host.Source);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task TestEscapeExitsEditorAsync(CancellationToken cancellationToken)
    {
        var terminal = new RecordingConsoleTerminal();
        DomRuntime.Initialize(new ConsoleDomBackend(terminal));
        try {
            using var world = new World();
            var registry = new BrowserEditorRegistry(world, new StaticCompilationReferenceResolver());
            var container = DomElement.Create("div").Class("code").Class("code-edit");
            DomElement.Find("notebook").Append(container);
            registry.Add(container, "cell-1", "abc", []);
            DomRuntime.Flush();

            terminal.Enqueue(Key(ConsoleKey.Tab));
            terminal.Enqueue(Key(ConsoleKey.Enter));
            terminal.Enqueue(Key(ConsoleKey.Escape));
            terminal.Enqueue(Key(ConsoleKey.Q));

            var backend = (ConsoleDomBackend)DomRuntime.Backend;
            var payload = await backend.WaitForEventAsync(cancellationToken);

            AcceptanceAssert.Equal("quit", payload);
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static async Task DriveEventsAsync(
        ConsoleDomBackend backend,
        BrowserEditorRegistry registry,
        CancellationToken cancellationToken,
        int count)
    {
        for (var i = 0; i < count; i++) {
            var payload = await backend.WaitForEventAsync(cancellationToken);
            registry.Route(payload);
            backend.Flush();
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key)
        => new('\0', key, false, false, false);

    private static ConsoleKeyInfo Char(char character)
        => new(character, ConsoleKey.NoName, false, false, false);

    private sealed class RecordingConsoleTerminal(int width = 80, int height = 16) : IConsoleTerminal
    {
        private readonly Queue<ConsoleKeyInfo> _keys = [];

        public int Width => width;

        public int Height => height;

        public IReadOnlyList<string> Rows { get; private set; } = [];

        public void Enqueue(ConsoleKeyInfo key) => _keys.Enqueue(key);

        public async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
        {
            while (_keys.Count == 0) {
                await Task.Delay(10, cancellationToken);
            }
            return _keys.Dequeue();
        }

        public void Draw(IReadOnlyList<string> rows) => Rows = [.. rows];

        public void Dispose()
        {
        }
    }
}
#endif
