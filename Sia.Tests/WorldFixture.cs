namespace Sia.Tests;

public partial record struct Padding1;

public partial record struct Padding2;

public partial record struct Padding3;

public partial record struct Padding4;

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
        for (var i = 0; i < count; ++i) {
            if (padding) {
                switch (i % 4) {
                    case 0: World.CreateInArrayHost(HList.Create(item1, new Padding1())); break;
                    case 1: World.CreateInArrayHost(HList.Create(item1, new Padding2())); break;
                    case 2: World.CreateInArrayHost(HList.Create(item1, new Padding3())); break;
                    case 3: World.CreateInArrayHost(HList.Create(item1, new Padding4())); break;
                }
            }
            else {
                World.CreateInArrayHost(HList.Create(item1));
            }
        }
    }

    public void Prepare<T1, T2>(in T1 item1, in T2 item2, int count, bool padding = true)
    {
        for (var i = 0; i < count; ++i) {
            if (padding) {
                switch (i % 4) {
                    case 0: World.CreateInArrayHost(HList.Create(item1, item2, new Padding1())); break;
                    case 1: World.CreateInArrayHost(HList.Create(item1, item2, new Padding2())); break;
                    case 2: World.CreateInArrayHost(HList.Create(item1, item2, new Padding3())); break;
                    case 3: World.CreateInArrayHost(HList.Create(item1, item2, new Padding4())); break;
                }
            }
            else {
                World.CreateInArrayHost(HList.Create(item1, item2));
            }
        }
    }

    public void Dispose() => World.Dispose();
}