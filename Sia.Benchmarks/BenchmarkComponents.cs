namespace Sia.Benchmarks;

internal struct Position(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

internal readonly record struct Velocity(float X, float Y, float Z);
internal readonly record struct Health(int Value);
internal readonly record struct BenchmarkEvent(int Value) : IEvent;

public enum AccessPattern
{
    Sequential,
    Permuted
}

public enum DispatchRoute
{
    Typed,
    Global,
    Target
}

internal readonly record struct ManagedPayload(string Value);

internal readonly record struct Padding1(long Value);
internal readonly record struct Padding2(long Value);
internal readonly record struct Padding3(long Value);
internal readonly record struct Padding4(long Value);

internal static class BenchmarkWorld
{
    public static World Create(int entityCount, int archetypeCount = 1, bool managed = false)
    {
        if (archetypeCount is not (1 or 4)) {
            throw new ArgumentOutOfRangeException(nameof(archetypeCount));
        }

        var world = new World();
        for (var i = 0; i < entityCount; i++) {
            var position = new Position(i, i + 1, i + 2);
            var velocity = new Velocity(1, 2, 3);
            var archetype = archetypeCount == 1 ? 0 : i & 3;
            if (managed) {
                var payload = new ManagedPayload("kept");
                switch (archetype) {
                    case 0: world.Create(HList.From(position, velocity, new Padding1(), payload)); break;
                    case 1: world.Create(HList.From(position, velocity, new Padding2(), payload)); break;
                    case 2: world.Create(HList.From(position, velocity, new Padding3(), payload)); break;
                    default: world.Create(HList.From(position, velocity, new Padding4(), payload)); break;
                }
            }
            else {
                switch (archetype) {
                    case 0: world.Create(HList.From(position, velocity, new Padding1())); break;
                    case 1: world.Create(HList.From(position, velocity, new Padding2())); break;
                    case 2: world.Create(HList.From(position, velocity, new Padding3())); break;
                    default: world.Create(HList.From(position, velocity, new Padding4())); break;
                }
            }
        }
        return world;
    }

    public static Entity[] Snapshot(World world)
        => [.. world.Query(Matchers.Of<Position>())];
}
