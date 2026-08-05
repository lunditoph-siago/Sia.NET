using Sia;

namespace Sia_Examples.Editor;

public static class CommandBridge
{
    private sealed class TempContext : IDisposable
    {
        public readonly World World = new();
        public readonly Entity Entity;
        private readonly World? _prev;

        public TempContext(EditorDoc doc, CursorState cursor)
        {
            _prev = Context<World>.Current;
            Context<World>.Current = World;
            Entity = World.Create(HList.From(doc, cursor));
        }

        public (EditorDoc Doc, CursorState Cursor) Apply(ICommand cmd)
        {
            World.Execute(Entity, cmd);
            return (Entity.Get<EditorDoc>(), Entity.Get<CursorState>());
        }

        public void Dispose()
        {
            Context<World>.Current = _prev;
            World.Dispose();
        }
    }

    public static (EditorDoc Doc, CursorState Cursor) Apply(
        ICommand cmd, EditorDoc doc, CursorState cursor)
    {
        using var ctx = new TempContext(doc, cursor);
        return ctx.Apply(cmd);
    }
}
