#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia;
using Sia.Reactive;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    public static Task Run() => BrowserExampleApp.Run(_library);
}

public static class BrowserExampleApp
{
    public static async Task Run(NotebookLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        using var world = new World();
        Context<World>.Current = world;

        using var host = new BrowserHost();
        var app = world.Mount(ExampleApp.Definition, new(library, host));

        NotebookSession? session = null;
        NotebookDocument? document = null;

        try {
            while (true) {
                var clickTask = host.WaitForClick();
                var editorTask = BrowserDom.WaitForEditorEvent();
                var completed = await Task.WhenAny(clickTask, editorTask);

                string evt;
                if (completed == editorTask)
                {
                    evt = await editorTask;
                    HandleEditorEvent(evt, session);
                    continue;
                }
                else
                {
                    evt = await clickTask;
                }

                var (kind, arg) = Split(evt);

                switch (kind) {
                    case "select": {
                        if (!int.TryParse(arg, out var index) || index < 0 || index >= library.Notebooks.Count) {
                            continue;
                        }

                        session?.Dispose();
                        BrowserEditorHost.DisposeAll();
                        var info = library.Notebooks[index];
                        document = library.Load(info);
                        session = new NotebookSession(document, new MetadataReferenceProvider());
                        session.Changed += () => {
                            host.ShowNotebook(NotebookRenderer.Render(document, session));
                            BrowserEditorHost.AttachAll();
                        };
                        await session.EnsureHighlightsAsync();
                        host.ShowNotebook(NotebookRenderer.Render(document, session));
                        BrowserEditorHost.AttachAll();

                        var state = app.GetState<ExampleAppState>();
                        state.Update(_ => new(index));
                        world.FlushReactive();
                        break;
                    }

                    case "compile" when session is not null:
                        await SyncEditableCells(session, host).ConfigureAwait(false);
                        _ = session.CompileThroughAsync(arg);
                        break;

                    case "run" when session is not null:
                        await SyncEditableCells(session, host).ConfigureAwait(false);
                        _ = session.RunThroughAsync(arg);
                        break;

                    case "stop" when session is not null:
                        session.Interrupt();
                        break;

                    case "save" when session is not null:
                        var source = host.ReadCellSource(arg);
                        _ = session.UpdateCellSourceAsync(arg, source);
                        break;
                }
            }
        }
        finally {
            session?.Dispose();
            if (app.IsMounted) {
                app.Unmount();
            }
        }
    }

    private static (string Kind, string Arg) Split(string evt)
    {
        var i = evt.IndexOf(':');
        return i < 0 ? (evt, "") : (evt[..i], evt[(i + 1)..]);
    }

    private static async Task SyncEditableCells(NotebookSession session, BrowserHost host)
    {
        foreach (var cell in session.Cells) {
            if (!cell.Editable) {
                continue;
            }
            var current = host.ReadCellSource(cell.Id);
            if (current != session.GetState(cell.Id).Source) {
                await session.UpdateCellSourceAsync(cell.Id, current).ConfigureAwait(false);
            }
        }
    }

    private static void HandleEditorEvent(string evt, NotebookSession? session)
    {
        var parts = evt.Split(':', 3);
        if (parts.Length < 3) return;

        var type = parts[0];
        var cellId = parts[1];
        var rest = parts[2];

        if (type == "key")
        {
            var keyParts = rest.Split(':');
            if (keyParts.Length < 4) return;
            var key = keyParts[0];
            var ctrl = bool.Parse(keyParts[1]);
            var shift = bool.Parse(keyParts[2]);

            BrowserEditorHost.OnEditorKeyDown(cellId, key, ctrl, shift, false);
        }
        else if (type == "input")
        {
            BrowserEditorHost.OnEditorChanged(cellId, rest);
        }
    }
}

public sealed class BrowserHost : IExampleRenderHost, IDisposable
{
    private readonly BrowserElement _sidebar = BrowserElement.Find("sidebar");
    private readonly BrowserElement _notebookContainer = BrowserElement.Find("notebook");
    private readonly SortedDictionary<int, ExampleNode> _examples = [];
    private BrowserElement? _notebookContent;

    public Task<string> WaitForClick() => BrowserDom.WaitForEvent();

    public void ShowNotebook(BrowserElement content)
    {
        _notebookContent?.Remove();
        _notebookContent?.Dispose();
        _notebookContainer.Text("");
        _notebookContent = content;
        _notebookContainer.Append(content);
    }

    public string ReadCellSource(string cellId)
    {
        var editorSource = Editor.BrowserEditorHost.ReadSource(cellId);
        if (editorSource != null) return editorSource;
        using var textarea = BrowserElement.Find(NotebookRenderer.SourceElementId(cellId));
        return textarea.Value();
    }

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

    public void Commit() { }

    public void Dispose()
    {
        foreach (var node in _examples.Values) {
            node.Dispose();
        }
        _examples.Clear();
        _notebookContent?.Dispose();
        _notebookContainer.Dispose();
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

public sealed class ExampleNode(
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
            .On("click", "select:" + index);
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

public sealed class BrowserElement(JSObject handle) : IDisposable
{
    internal JSObject Handle { get; } = handle;

    public static BrowserElement Find(string id) => new(BrowserDom.Find(id));
    public static BrowserElement Create(string tag) => new(BrowserDom.Create(tag));
    public static BrowserElement CreateText(string value) => new(BrowserDom.CreateText(value));

    public BrowserElement Class(string name) => ToggleClass(name, true);

    public BrowserElement Text(string value)
    {
        BrowserDom.SetText(Handle, value);
        return this;
    }

    public BrowserElement Id(string id)
    {
        BrowserDom.SetId(Handle, id);
        return this;
    }

    public BrowserElement Position(double top, double left)
    {
        BrowserDom.SetPosition(Handle, top, left);
        return this;
    }

    public string Value() => BrowserDom.GetValue(Handle);

    public BrowserElement ToggleClass(string name, bool enabled)
    {
        BrowserDom.ToggleClass(Handle, name, enabled);
        return this;
    }

    public BrowserElement On(string name, string payload)
    {
        BrowserDom.Listen(Handle, name, payload);
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

    [JSImport("createText", "main.js")]
    internal static partial JSObject CreateText(string value);

    [JSImport("setText", "main.js")]
    internal static partial void SetText(JSObject element, string value);

    [JSImport("getValue", "main.js")]
    internal static partial string GetValue(JSObject element);

    [JSImport("setId", "main.js")]
    internal static partial void SetId(JSObject element, string id);

    [JSImport("setPosition", "main.js")]
    internal static partial void SetPosition(JSObject element, double top, double left);

    [JSImport("toggleClass", "main.js")]
    internal static partial void ToggleClass(JSObject element, string name, bool enabled);

    [JSImport("listen", "main.js")]
    internal static partial void Listen(JSObject element, string name, string payload);

    [JSImport("insertBefore", "main.js")]
    internal static partial void InsertBefore(
        JSObject parent,
        JSObject child,
        JSObject? before);

    [JSImport("remove", "main.js")]
    internal static partial void Remove(JSObject element);

    [JSImport("waitForEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> WaitForEvent();

    [JSImport("waitForEditorEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> WaitForEditorEvent();
}
#endif
