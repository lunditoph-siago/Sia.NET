#if !BROWSER
using Sia;
using Sia.Reactive;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    public static void Run(IReadOnlyList<string> arguments)
        => ConsoleExampleApp.Run(_runner, arguments);
}

public static class ConsoleExampleApp
{
    public static void Run(ExampleRunner runner, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(arguments);

        using var world = new World();
        Context<World>.Current = world;

        var host = new ConsoleHost(SuppressInitialCommit(arguments));
        var app = world.Mount(
            ExampleApp.Definition,
            new(runner, host, ExampleAppState.Initial));

        try {
            if (arguments.Count > 0) {
                RunFromArguments(runner, app, world, arguments);
                return;
            }

            while (true) {
                Console.Write("\nSelect an example (0 quits): ");
                if (!int.TryParse(Console.ReadLine(), out var choice)) {
                    continue;
                }
                if (choice == 0) {
                    return;
                }

                var index = choice - 1;
                if (index < 0 || index >= runner.Examples.Count) {
                    continue;
                }

                var example = runner.Examples[index];
                app.Update(app.Props with {
                    State = app.Props.State.Begin(index, example.Name)
                });
                world.FlushReactive();

                app.Update(app.Props with {
                    State = app.Props.State.Complete(runner.RunExample(index))
                });
                world.FlushReactive();

                Console.Write("\nPress any key to return to the examples\u2026");
                Console.ReadKey(true);
            }
        }
        finally {
            if (app.IsMounted) {
                app.Unmount();
            }
        }
    }

    private static void RunFromArguments(
        ExampleRunner runner,
        ReactiveMount<ExampleAppProps> app,
        World world,
        IReadOnlyList<string> arguments)
    {
        var selector = arguments[0] == "--example" && arguments.Count > 1
            ? arguments[1]
            : arguments[0];
        if (selector is "--list" or "list") {
            return;
        }

        var index = ResolveExample(runner.Examples, selector);
        if (index < 0) {
            Console.Error.WriteLine($"Unknown example '{selector}'. Use --list to inspect choices.");
            Environment.ExitCode = 1;
            return;
        }

        var example = runner.Examples[index];
        app.Update(app.Props with {
            State = app.Props.State.Begin(index, example.Name)
        });
        app.Update(app.Props with {
            State = app.Props.State.Complete(runner.RunExample(index))
        });
        world.FlushReactive();
    }

    private static bool SuppressInitialCommit(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && arguments[0] is not ("--list" or "list");

    private static int ResolveExample(
        IReadOnlyList<ExampleRunner.ExampleInfo> examples,
        string selector)
    {
        if (int.TryParse(selector, out var number)) {
            return number > 0 && number <= examples.Count ? number - 1 : -1;
        }

        for (var index = 0; index < examples.Count; index++) {
            if (string.Equals(
                examples[index].Name,
                selector,
                StringComparison.OrdinalIgnoreCase)) {
                return index;
            }
        }
        return -1;
    }
}

public sealed class ConsoleHost(bool suppressNextCommit) : IExampleRenderHost
{
    private readonly SortedDictionary<int, ExampleItemView> _items = [];
    private bool _suppressNextCommit = suppressNextCommit;
    private ExampleOutputView _output;
    private bool _hasOutput;

    public void Upsert(in ExampleItemView view) => _items[view.Index] = view;
    public void Remove(in ExampleItemView view) => _items.Remove(view.Index);

    public void Upsert(in ExampleOutputView view)
        => (_output, _hasOutput) = (view, true);

    public void Remove(in ExampleOutputView view)
        => (_output, _hasOutput) = (default, false);

    public void Commit()
    {
        if (_suppressNextCommit) {
            _suppressNextCommit = false;
            return;
        }
        if (!Console.IsOutputRedirected) {
            Console.Clear();
        }
        Console.WriteLine("\u250c\u2500 Sia.NET Examples \u2500────────────────────────────────────────────");

        foreach (var item in _items.Values) {
            var marker = item.Active ? "\u25cf" : " ";
            Console.WriteLine(
                $"\u2502 {marker} [{item.Index + 1,2}] {item.Name,-20} {item.Description}");
        }

        Console.WriteLine("\u251c────────────────────────────────────────────────────────────");
        if (_hasOutput) {
            Console.WriteLine($"\u2502 {_output.Title}{(_output.Loading ? "  \u25cc" : "")}");
            Console.WriteLine("\u2514────────────────────────────────────────────────────────────");
            Console.Write(_output.Output);
        }
    }
}
#endif
