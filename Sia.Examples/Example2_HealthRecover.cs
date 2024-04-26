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
            public void Execute(World _, in EntityRef target)
                => target.Get<HP>().Value -= Value;
        }

        public sealed class Kill : SingletonEvent<Kill>, ICancellableEvent;
    }

    public class HPAutoRecoverSystem()
        : SystemBase(Matchers.Of<HP>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var hp = ref entity.Get<HP>();

                if (hp.Value < hp.Maximum) {
                    hp.Value = Math.Min(hp.Value + hp.AutoRecoverRate, hp.Maximum);
                    Console.WriteLine("血量已自动回复。");
                }
                else {
                    Console.WriteLine("血量已满，未自动回复。");
                }
            }
        }
    }

    public class DamageDisplaySystem()
        : SystemBase(
            Matchers.Of<HP, Name>(),
            EventUnion.Of<HP.Damage>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            foreach (var entity in query) {
                Console.WriteLine($"[{entity.Get<Name>().Value}] 受到攻击！");
            }
        }
    }

    public class KillSystem()
        : SystemBase(
            Matchers.Of<HP>(),
            EventUnion.Of<HP.Kill>(),
            filter: EventUnion.Of<HOEvents.Cancel<HP.Kill>>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
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
        public static EntityRef CreateResilient(World world, string name)
            => world.CreateInArrayHost(HList.Create(
                new Name(name),
                new HP {
                    AutoRecoverRate = 10
                }
            ));
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<DamageDisplaySystem>()
            .Add<HPAutoRecoverSystem>()
            .Add<KillSystem>()
            .RegisterTo(world, scheduler);

        var player = Player.CreateResilient(world, "玩家");
        ref var hp = ref player.Get<HP>();

        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();

        world.Modify(player, new HP.Damage(50));
        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();
        Console.WriteLine("HP: " + hp.Value);

        world.Send(player, HP.Kill.Instance);
        scheduler.Tick();
        Console.WriteLine("HP: " + hp.Value);

        hp.Value = 100;
        world.Send(player, HP.Kill.Instance);
        world.Send(player, HOEvents.Cancel<HP.Kill>.Instance);
        scheduler.Tick();
        Console.WriteLine("HP: " + hp.Value);
    }
}