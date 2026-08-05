using Sia;

namespace Sia_Examples.Editor;

public static class CommandBridge
{
    private sealed class TempContext : IDisposable
    {
        public readonly World World = new();
        public readonly Entity Entity;

        public TempContext(EditorDoc doc, CursorState cursor)
        {
            Context<World>.Current = World;
            Entity = World.Create(HList.From(doc, cursor));
        }

        public (EditorDoc Doc, CursorState Cursor) Apply(ICommand cmd)
        {
            World.Execute(Entity, cmd);
            return (Entity.Get<EditorDoc>(), Entity.Get<CursorState>());
        }

        public void Dispose() => World.Dispose();
    }

    public static (EditorDoc Doc, CursorState Cursor) Apply(
        ICommand cmd, EditorDoc doc, CursorState cursor)
    {
        using var ctx = new TempContext(doc, cursor);
        return ctx.Apply(cmd);
    }
}
