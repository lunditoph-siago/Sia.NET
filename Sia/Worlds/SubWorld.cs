namespace Sia;

using System.Runtime.CompilerServices;

public sealed class SubWorldContext : IAddon
{
    public World Parent { get; set; }
}

public sealed class SubWorld : IDisposable
{
    public World Parent { get; }

    public World World { get; }

    public bool IsDisposed { get; private set; }

    public SubWorld(World parent)
    {
        Parent = parent;
        World = new World();

        var context = World.AddAddon<SubWorldContext>();
        context.Parent = parent;
    }

    ~SubWorld()
    {
        DoDispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(SystemStage stage)
        => Context<World>.With(World, stage.Tick);

    public void Dispose()
    {
        DoDispose();
        GC.SuppressFinalize(this);
    }

    private void DoDispose()
    {
        if (IsDisposed) { return; }
        IsDisposed = true;
        World.Dispose();
    }
}

public sealed class SubWorldAddon : IAddon, IDisposable
{
    public SubWorld SubWorld { get; private set; } = null!;

    void IAddon.OnInitialize(World world)
        => SubWorld = new SubWorld(world);

    void IAddon.OnUninitialize(World world)
        => SubWorld.Dispose();

    public void Dispose()
        => SubWorld.Dispose();
}
