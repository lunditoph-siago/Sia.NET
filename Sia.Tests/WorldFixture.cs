namespace Sia.Tests.Systems;

public class WorldFixture : IDisposable
{
    public World World { get; }

    public WorldFixture()
    {
        World = new World();
        Context<World>.Current = World;
    }

    public void Prepare<T1>(in T1 item1, int count, bool padding = true)
    {
        for (var i = 0; i < count; ++i)
        {
            if (padding)
            {
                switch (i % 4)
                {
                    case 0:
                        World.CreateInArrayHost(Bundle.Create(item1, new SystemBaseTests.Padding1()));
                        break;
                    case 1:
                        World.CreateInArrayHost(Bundle.Create(item1, new SystemBaseTests.Padding2()));
                        break;
                    case 2:
                        World.CreateInArrayHost(Bundle.Create(item1, new SystemBaseTests.Padding3()));
                        break;
                    case 3:
                        World.CreateInArrayHost(Bundle.Create(item1, new SystemBaseTests.Padding4()));
                        break;
                }
            }
            else
            {
                World.CreateInArrayHost(Bundle.Create(item1));
            }
        }
    }

    public void Prepare<T1, T2>(in T1 item1, in T2 item2, int count, bool padding = true)
    {
        for (var i = 0; i < count; ++i)
        {
            if (padding)
            {
                switch (i % 4)
                {
                    case 0:
                        World.CreateInArrayHost(Bundle.Create(item1, item2, new SystemBaseTests.Padding1()));
                        break;
                    case 1:
                        World.CreateInArrayHost(Bundle.Create(item1, item2, new SystemBaseTests.Padding2()));
                        break;
                    case 2:
                        World.CreateInArrayHost(Bundle.Create(item1, item2, new SystemBaseTests.Padding3()));
                        break;
                    case 3:
                        World.CreateInArrayHost(Bundle.Create(item1, item2, new SystemBaseTests.Padding4()));
                        break;
                }
            }
            else
            {
                World.CreateInArrayHost(Bundle.Create(item1, item2));
            }
        }
    }

    public void Dispose() => World.Dispose();
}