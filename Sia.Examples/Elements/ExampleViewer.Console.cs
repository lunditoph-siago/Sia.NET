#if !BROWSER
using Sia;
using Sia.Reactive;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    public static void Run(IReadOnlyList<string> arguments)
        => ConsoleExampleApp.Run(_library, arguments);
}

public static class ConsoleExampleApp
{
    public static void Run(NotebookLibrary library, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count > 0 || Console.IsOutputRedirected || Console.IsInputRedirected) {
            RunFromArguments(library, arguments.Count > 0 ? arguments : ["--list"]);
            return;
        }

        using var world = new World();
        Context<World>.Current = world;

        using var screen = new ConsoleScreen();
        var host = new ConsoleHost(screen);
        var app = world.Mount(ExampleApp.Definition, new(library, host));
        host.Commit();

        try {
            while (true) {
                host.SetStatus("Select a notebook (type its number), or q to quit.");
                var input = ReadLine(screen);
                if (input is "q" or "quit") {
                    return;
                }
                if (!int.TryParse(input, out var choice)) {
                    continue;
                }

                var index = choice - 1;
                if (index < 0 || index >= library.Notebooks.Count) {
                    continue;
                }

                var state = app.GetState<ExampleAppState>();
                state.Update(_ => new(index));
                world.FlushReactive();

                RunNotebook(library, host, screen, index);

                state.Update(_ => ExampleAppState.Initial);
                world.FlushReactive();
                host.ClearWorkspace();
            }
        }
        finally {
            if (app.IsMounted) {
                app.Unmount();
            }
        }
    }

    private static void RunNotebook(NotebookLibrary library, ConsoleHost host, ConsoleScreen screen, int index)
    {
        var info = library.Notebooks[index];
        var document = library.Load(info);
        var references = new MetadataReferenceProvider();
        using var session = new NotebookSession(document, references);
        session.EnsurePackagesAsync().GetAwaiter().GetResult();

        var scroll = 0;

        void Redraw() => host.ShowWorkspace(
            NotebookRenderer.RenderLines(document, session), scroll);

        session.Changed += Redraw;
        try {
            session.EnsureHighlightsAsync().GetAwaiter().GetResult();
            Redraw();

            while (true) {
                host.SetStatus("c<N> compile  r<N> run  e<N> edit  u/d scroll  s stop  b back  q quit  pkg <src> <id>");
                var input = ReadLine(screen);
                if (input.Length == 0) {
                    continue;
                }
                if (input is "b" or "back") {
                    return;
                }
                if (input is "q" or "quit") {
                    Environment.Exit(0);
                }
                if (input is "s" or "stop") {
                    session.Interrupt();
                    continue;
                }
                if (input is "u" or "up") {
                    scroll = Math.Max(scroll - 5, 0);
                    Redraw();
                    continue;
                }
                if (input is "d" or "down") {
                    scroll += 5;
                    Redraw();
                    continue;
                }
                if (input.StartsWith("pkg ", StringComparison.OrdinalIgnoreCase)) {
                    var parts = input[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || (parts[0].ToLowerInvariant() is not ("nuget" or "framework"))) {
                        host.SetStatus("Usage: pkg <nuget|framework> <id> [version]");
                        continue;
                    }
                    var packageSource = parts[0].Equals("nuget", StringComparison.OrdinalIgnoreCase)
                        ? PackageSource.NuGet : PackageSource.Framework;
                    var version = parts.Length > 2 ? parts[2] : null;
                    RunBlocking(session.AddPackageAsync(new PackageRef(packageSource, parts[1], version)));
                    continue;
                }

                var action = input[0];
                if (!int.TryParse(input[1..], out var number) || number < 1 || number > session.Cells.Count) {
                    host.SetStatus("Unrecognized command. Try c<N>, r<N>, e<N>, s, u, d, or b.");
                    continue;
                }
                var cell = session.Cells[number - 1];

                if (session.IsBusy) {
                    host.SetStatus("A compile or run is already in progress — type s to stop it first.");
                    continue;
                }

                switch (action) {
                    case 'c':
                        RunBlocking(session.CompileThroughAsync(cell.Id));
                        break;
                    case 'r':

                        RunBlocking(session.RunThroughAsync(cell.Id));
                        break;
                    case 'e':
                        using (var editor = new Sia_Examples.Editor.ConsoleEditorHost(
                            screen, cell.Id, session.GetState(cell.Id).Source,
                            session.GetState(cell.Id).Highlights, session.References))
                        {
                            var result = editor.Edit(host.BuildSidebarLines(), ref scroll);
                            if (result != null)
                                RunBlocking(session.UpdateCellSourceAsync(cell.Id, result));
                        }
                        Redraw();
                        break;
                    default:
                        host.SetStatus("Unrecognized command. Try c<N>, r<N>, e<N>, s, u, d, or b.");
                        break;
                }
            }
        }
        finally {
            session.Changed -= Redraw;
        }
    }

    private static string ReadLine(ConsoleScreen screen)
    {
        var layout = new SplitPaneRenderer(screen).Layout();
        screen.ShowCursorAt(layout.InputRow, 0);
        var input = (Console.ReadLine() ?? "").Trim();
        screen.HideCursor();
        return input;
    }

    private static void RunBlocking(Task task)
    {
        try {
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            Console.Error.WriteLine(ex);
        }
    }

    private static void RunFromArguments(NotebookLibrary library, IReadOnlyList<string> arguments)
    {
        var selector = arguments[0] == "--example" && arguments.Count > 1
            ? arguments[1]
            : arguments[0];
        if (selector is "--list" or "list") {
            foreach (var notebook in library.Notebooks) {
                Console.WriteLine($"{notebook.Name} — {notebook.Description}");
            }
            return;
        }

        var index = ResolveNotebook(library, selector);
        if (index < 0) {
            Console.Error.WriteLine($"Unknown notebook '{selector}'. Use --list to inspect choices.");
            Environment.ExitCode = 1;
            return;
        }

        var info = library.Notebooks[index];
        var document = library.Load(info);
        var references = new MetadataReferenceProvider();
        using var session = new NotebookSession(document, references);
        session.EnsurePackagesAsync().GetAwaiter().GetResult();

        Dictionary<string, string> seenScopeLastCell = [];
        foreach (var cell in session.Cells) {
            var scopeKey = cell.Scope ?? $"$solo:{cell.Id}";
            seenScopeLastCell[scopeKey] = cell.Id;
        }
        foreach (var lastCellId in seenScopeLastCell.Values) {
            session.RunThroughAsync(lastCellId).GetAwaiter().GetResult();
        }

        foreach (var line in NotebookRenderer.RenderLines(document, session)) {
            Console.WriteLine(line);
        }
    }

    private static int ResolveNotebook(NotebookLibrary library, string selector)
    {
        var notebooks = library.Notebooks;
        if (int.TryParse(selector, out var number)) {
            return number > 0 && number <= notebooks.Count ? number - 1 : -1;
        }
        for (var index = 0; index < notebooks.Count; index++) {
            if (string.Equals(notebooks[index].Name, selector, StringComparison.OrdinalIgnoreCase)) {
                return index;
            }
        }
        return -1;
    }
}

internal sealed class ConsoleHost(ConsoleScreen screen) : IExampleRenderHost
{
    private readonly SortedDictionary<int, ExampleItemView> _items = [];
    private readonly SplitPaneRenderer _renderer = new(screen);
    private IReadOnlyList<string> _workspaceLines = [];
    private string _status = "";
    private int _scroll;

    public void Upsert(in ExampleItemView view) => _items[view.Index] = view;
    public void Remove(in ExampleItemView view) => _items.Remove(view.Index);

    public void SetStatus(string status)
    {
        _status = status;
        Commit();
    }

    public void ShowWorkspace(IReadOnlyList<string> lines, int scroll)
    {
        _workspaceLines = lines;
        _scroll = Math.Clamp(scroll, 0, Math.Max(lines.Count - 1, 0));
        Commit();
    }

    public void ClearWorkspace()
    {
        _workspaceLines = [];
        _scroll = 0;
        Commit();
    }

    public IReadOnlyList<string> BuildSidebarLines()
    {
        var sidebar = new List<string> { "Notebooks", "" };
        foreach (var item in _items.Values) {
            var marker = item.Active ? "▶ " : "  ";
            sidebar.Add($"{marker}[{item.Index + 1}] {item.Name}");
            sidebar.Add($"      {item.Description}");
        }
        return sidebar;
    }

    public void Commit()
    {
        var visible = _workspaceLines.Skip(_scroll).ToList();
        _renderer.Render(BuildSidebarLines(), visible, _status);
    }
}
#endif
