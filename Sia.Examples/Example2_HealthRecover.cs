namespace Sia.Examples;

using Sia;

public static class Example2_HealthRecover
{
    public record struct Name(string Value);

    public record struct HP(
        int Value, int Maximum, int AutoRecoverRate)
    {
        public readonly record struct Damage(int Value) : ICommand
        {
            public void Execute(World _, in EntityRef target)
                => target.Get<HP>().Value -= Value;
        }

        public HP() : this(100, 100, 0) {}
    }

    public class HPAutoRecoverSystem : SystemBase
    {
        public HPAutoRecoverSystem()
        {
            Matcher = Matchers.Of<HP>();
        }

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                ref var hp = ref entity.Get<HP>();

                if (hp.Value < hp.Maximum) {
                    hp.Value = Math.Min(hp.Value + hp.AutoRecoverRate, hp.Maximum);
                    Console.WriteLine("血量已自动回复。");
                }
                else {
                    Console.WriteLine("血量已满，未自动回复。");
                }
            });
        }
    }

    public class DamageDisplaySystem : SystemBase
    {
        public DamageDisplaySystem()
        {
            Matcher = Matchers.Of<HP, Name>();
            Trigger = EventUnion.Of<HP.Damage>();
        }

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                Console.WriteLine($"[{entity.Get<Name>().Value}] 受到攻击！");
            });
        }
    }

    public static class Player
    {
        public static EntityRef CreateResilient(World world, string name)
            => world.CreateInHashHost(Tuple.Create(
                new Name(name),
                new HP {
                    AutoRecoverRate = 10
                }
            ));
    }

    public static void Run()
    {
        var world = new World();
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<DamageDisplaySystem>()
            .Add<HPAutoRecoverSystem>()
            .RegisterTo(world, scheduler);

        var player = Player.CreateResilient(world, "玩家");
        ref var hp = ref player.Get<HP>();

        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();

        world.Modify(player, new HP.Damage(50));
        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();
        Console.WriteLine("HP: " + hp.Value);
    }
}