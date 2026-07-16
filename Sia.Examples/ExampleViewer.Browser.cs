#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia;
using Sia.Reactive;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    public static Task Run() => BrowserExampleApp.Run(_runner);
}

internal static class BrowserExampleApp
{
    public static async Task Run(ExampleRunner runner)
    {
        using var world = new World();
        Context<World>.Current = world;

        var controller = new ExampleController();
        var reconciler = world.AcquireAddon<Reconciler>();
        using var host = new BrowserHost();
        var app = reconciler.Mount(new ExampleApp(runner, controller, host));

        try {
            while (true) {
                var index = await host.WaitForClick();
                if (index < 0 || index >= runner.Examples.Count) {
                    continue;
                }

                var example = runner.Examples[index];
                controller.BeginRun(index, example.Name);
                reconciler.Flush();

                // Let the browser paint the pending state before the
                // synchronous example starts producing its output.
                await Task.Delay(1);

                controller.CompleteRun(runner.RunExample(index));
                reconciler.Flush();
            }
        }
        finally {
            if (app.IsMounted) {
                app.Unmount();
            }
        }
    }
}

internal sealed class BrowserHost : IExampleRenderHost, IDisposable
{
    private readonly BrowserElement _sidebar = BrowserElement.Find("sidebar");
    private readonly BrowserElement _output = BrowserElement.Find("output");
    private readonly BrowserElement _outputTitle = BrowserElement.Find("output-title");
    private readonly SortedDictionary<int, ExampleNode> _examples = [];

    public Task<int> WaitForClick() => BrowserDom.WaitForEvent();

    public void Upsert(in ExampleItemView view)
    {
        if (!_examples.TryGetValue(view.Index, out var node)) {
            node = ExampleNode.Create(view.Index);
            _examples.Add(view.Index, node);
            _sidebar.InsertBefore(node.Root, FindNext(view.Index));
        }

        node.Render(view);
    }

    public void Remove(in ExampleItemView view)
    {
        if (_examples.Remove(view.Index, out var node)) {
            node.Dispose();
        }
    }

    public void Upsert(in ExampleOutputView view)
    {
        _outputTitle.Text(view.Title);
        _output.Text(view.Output).ToggleClass("loading", view.Loading);
    }

    public void Remove(in ExampleOutputView view)
    {
        _outputTitle.Text("");
        _output.Text("").ToggleClass("loading", false);
    }

    public void Commit() { }

    public void Dispose()
    {
        foreach (var node in _examples.Values) {
            node.Dispose();
        }
        _examples.Clear();
        _outputTitle.Dispose();
        _output.Dispose();
        _sidebar.Dispose();
    }

    private BrowserElement? FindNext(int index)
    {
        foreach (var (key, node) in _examples) {
            if (key > index) {
                return node.Root;
            }
        }
        return null;
    }
}

internal sealed class ExampleNode(
    BrowserElement root,
    BrowserElement name,
    BrowserElement description) : IDisposable
{
    public BrowserElement Root { get; } = root;
    private BrowserElement Name { get; } = name;
    private BrowserElement Description { get; } = description;

    public static ExampleNode Create(int index)
    {
        var root = BrowserElement.Create("button")
            .Class("example-btn")
            .On("click", index);
        var name = BrowserElement.Create("span").Class("name");
        var description = BrowserElement.Create("span").Class("desc");
        root.Append(name).Append(description);
        return new(root, name, description);
    }

    public void Render(in ExampleItemView view)
    {
        Name.Text(view.Name);
        Description.Text(view.Description);
        Root.ToggleClass("active", view.Active);
    }

    public void Dispose()
    {
        Root.Remove();
        Description.Dispose();
        Name.Dispose();
        Root.Dispose();
    }
}

internal sealed class BrowserElement(JSObject handle) : IDisposable
{
    internal JSObject Handle { get; } = handle;

    public static BrowserElement Find(string id) => new(BrowserDom.Find(id));
    public static BrowserElement Create(string tag) => new(BrowserDom.Create(tag));

    public BrowserElement Class(string name) => ToggleClass(name, true);

    public BrowserElement Text(string value)
    {
        BrowserDom.SetText(Handle, value);
        return this;
    }

    public BrowserElement ToggleClass(string name, bool enabled)
    {
        BrowserDom.ToggleClass(Handle, name, enabled);
        return this;
    }

    public BrowserElement On(string name, int eventId)
    {
        BrowserDom.Listen(Handle, name, eventId);
        return this;
    }

    public BrowserElement Append(BrowserElement child)
    {
        BrowserDom.InsertBefore(Handle, child.Handle, null);
        return this;
    }

    public void InsertBefore(BrowserElement child, BrowserElement? before)
        => BrowserDom.InsertBefore(Handle, child.Handle, before?.Handle);

    public void Remove() => BrowserDom.Remove(Handle);
    public void Dispose() => Handle.Dispose();
}

internal static partial class BrowserDom
{
    [JSImport("find", "main.js")]
    internal static partial JSObject Find(string id);

    [JSImport("create", "main.js")]
    internal static partial JSObject Create(string tag);

    [JSImport("setText", "main.js")]
    internal static partial void SetText(JSObject element, string value);

    [JSImport("toggleClass", "main.js")]
    internal static partial void ToggleClass(JSObject element, string name, bool enabled);

    [JSImport("listen", "main.js")]
    internal static partial void Listen(JSObject element, string name, int eventId);

    [JSImport("insertBefore", "main.js")]
    internal static partial void InsertBefore(
        JSObject parent,
        JSObject child,
        JSObject? before);

    [JSImport("remove", "main.js")]
    internal static partial void Remove(JSObject element);

    [JSImport("waitForEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<int> WaitForEvent();
}
#endif
