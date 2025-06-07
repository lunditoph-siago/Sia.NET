namespace Sia_Examples;

using Sia;

public static class Example2_HealthRecover
{
    public record struct Name(string Value);

    public record struct HP(
        int Value, int Maximum, int AutoRecoverRate)
    {
        public HP() : this(100, 100, 0) {}

        public readonly record struct Damage(int Value) : ICommand
        {
            public void Execute(World _, Entity target)
                => target.Get<HP>().Value -= Value;
        }

        public sealed class Kill : SingletonEvent<Kill>, ICancellableEvent;
    }

    public class HPAutoRecoverSystem() : SystemBase(
        Matchers.Of<HP>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var hp = ref entity.Get<HP>();

                if (hp.Value < hp.Maximum) {
                    hp.Value = Math.Min(hp.Value + hp.AutoRecoverRate, hp.Maximum);
                    Console.WriteLine("HP auto recovered.");
                }
                else {
                    Console.WriteLine("HP is full, no auto recovery.");
                }
            }
        }
    }

    public class DamageDisplaySystem() : SystemBase(
        Matchers.Of<HP, Name>(),
        EventUnion.Of<HP.Damage>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                Console.WriteLine($"[{entity.Get<Name>().Value}] Received damage!");
            }
        }
    }

    public class KillSystem() : SystemBase(
        Matchers.Of<HP>(),
        EventUnion.Of<HP.Kill>(),
        filter: EventUnion.Of<HOEvents.Cancel<HP.Kill>>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            Console.WriteLine("START KILLING!");

            foreach (var entity in query) {
                entity.Get<HP>().Value = 0;
                Console.WriteLine("KILL!");
            }
        }
    }

    public static class Player
    {
        public static Entity CreateResilient(World world, string name)
            => world.Create(HList.From(
                new Name(name),
                new HP {
                    AutoRecoverRate = 10
                }
            ));
    }

    public static void Run(World world)
    {
        var stage = SystemChain.Empty
            .Add<DamageDisplaySystem>()
            .Add<HPAutoRecoverSystem>()
            .Add<KillSystem>()
            .CreateStage(world);

        var player = Player.CreateResilient(world, "Player");
        ref var hp = ref player.Get<HP>();

        Console.WriteLine("HP: " + hp.Value);
        stage.Tick();

        world.Execute(player, new HP.Damage(50));
        Console.WriteLine("HP: " + hp.Value);
        stage.Tick();
        Console.WriteLine("HP: " + hp.Value);

        world.Send(player, HP.Kill.Instance);
        stage.Tick();
        Console.WriteLine("HP: " + hp.Value);

        hp.Value = 100;
        world.Send(player, HP.Kill.Instance);
        world.Send(player, HOEvents.Cancel<HP.Kill>.Instance);
        stage.Tick();
        Console.WriteLine("HP: " + hp.Value);
    }
}