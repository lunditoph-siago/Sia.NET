namespace Sia;

using System.Runtime.CompilerServices;

public sealed class SubWorldContext(World parent) : IAddon, IWorldSource
{
    public World Parent { get; } = parent;
    public World Source => Parent;
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

        World.AddAddon(new SubWorldContext(parent));
    }

    ~SubWorld()
    {
        DoDispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(SystemStage stage, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!ReferenceEquals(stage.World, World)) {
            throw new ArgumentException(
                "The stage was created for a different world.", nameof(stage));
        }

        stage.Tick(cancellation);
    }

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
