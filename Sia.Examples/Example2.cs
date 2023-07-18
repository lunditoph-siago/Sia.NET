namespace Sia.Examples;

using Sia;

public static class Example2
{
    public record struct Name(string Value)
    {
        public static implicit operator Name(string value)
            => new(value);
    }

    public record struct HP(
        int Value, int Maximum, int AutoRecoverRate)
    {
        public class Damage : Command<Damage>
        {
            public int Value;

            public static Damage Create(int value)
            {
                var cmd = CreateRaw();
                cmd.Value = value;
                return cmd;
            }

            public override void Execute(in EntityRef target)
            {
                target.Get<HP>().Value -= Value;
            }
        }
    }

    public class HPAutoRecoverSystem : SystemBase
    {
        public HPAutoRecoverSystem()
        {
            Matcher = new TypeUnion<HP>();
        }

        public override void Execute(World world, Scheduler scheduler, in EntityRef entity)
        {
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

    public class DamageDisplaySystem : SystemBase
    {
        public DamageDisplaySystem()
        {
            Matcher = new TypeUnion<HP, Name>();
            Trigger = new EventUnion<HP.Damage>();
        }

        public override void Execute(World world, Scheduler scheduler, in EntityRef entity)
        {
            Console.WriteLine($"[{entity.Get<Name>().Value}] 受到攻击！");
        }
    }

    public record struct Player(Name Name, HP HP);

    public static void Run()
    {
        var world = new World();
        var scheduler = new Scheduler();

        new DamageDisplaySystem().Register(world, scheduler);
        new HPAutoRecoverSystem().Register(world, scheduler);

        EntityRef player = EntityFactory<Player>.Default.Create(new() {
            Name = "玩家",
            HP = new() {
                Value = 100,
                Maximum = 100,
                AutoRecoverRate = 5
            }
        });
        world.Add(player);

        ref var hp = ref player.Get<HP>();

        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();

        world.Modify(player, HP.Damage.Create(50));
        Console.WriteLine("HP: " + hp.Value);
        scheduler.Tick();
        Console.WriteLine("HP: " + hp.Value);
    }
}