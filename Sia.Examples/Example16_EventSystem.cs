namespace Sia_Examples;

using Sia;

public static partial class Example16_EventSystem
{
    [SiaTemplate(nameof(Position))]
    public record RPosition(int X, int Y);

    public partial record struct Position
    {
        public readonly record struct MoveUp(int Count) : IEvent;
        public readonly record struct MoveDown(int Count) : IEvent;
        public readonly record struct MoveLeft(int Count) : IEvent;
        public readonly record struct MoveRight(int Count) : IEvent;

        public readonly record struct TestCommand : ICommand
        {
            public void Execute(World world, in EntityRef target)
                => target.Get<Position>().X++;
        }
    }

    public class TestEventSystem : EventSystemBase
    {
        public override void Initialize(World world, Scheduler scheduler)
        {
            base.Initialize(world, scheduler);
            world.IndexHosts(Matchers.Of<Position>());

            RecordEvent<Position.MoveUp>();
            RecordEvent<Position.MoveDown>();
            RecordEvent<Position.MoveLeft>();
            RecordEvent<Position.MoveRight>();
        }

        private Position.View Get(in Identity id)
            => new(World[id]);

        protected override void HandleEvent<TEvent>(in Identity id, in TEvent e)
        {
            switch (e) {
                case Position.MoveUp(int count):
                    Console.WriteLine("Move Up!");
                    Get(id).Y += count;
                    break;
                case Position.MoveDown(int count):
                    Console.WriteLine("Move Down!");
                    Get(id).Y -= count;
                    break;
                case Position.MoveLeft(int count):
                    Console.WriteLine("Move Left!");
                    Get(id).X -= count;
                    break;
                case Position.MoveRight(int count):
                    Console.WriteLine("Move Right!");
                    Get(id).X += count;
                    break;
            }
        }
    }

    public class TestSnapshotEventSystem : SnapshotEventSystemBase<Position>
    {
        public override void Initialize(World world, Scheduler scheduler)
        {
            base.Initialize(world, scheduler);

            RecordEvent<WorldEvents.Add<Position>>();
            RecordRemovalEvent<WorldEvents.Remove<Position>>();
            RecordEvent<Position.TestCommand>();
        }

        protected override Position Snapshot<TEvent>(in EntityRef entity, in TEvent e)
            => entity.Get<Position>();

        protected override void HandleEvent<TEvent>(
            in Identity id, in Position snapshot, in TEvent e)
        {
            switch (e) {
                case Position.TestCommand:
                    Console.WriteLine("Test Command!");
                    Console.WriteLine("\tPrevious X: " + snapshot.X);
                    break;
            }
        }
    }

    public class TestTemplateEventSystem : TemplateEventSystemBase<Position, RPosition>
    {
        protected override void HandleEvent<TEvent>(
            in Identity id, in Position snapshot, in TEvent e)
        {
            switch (e) {
                case WorldEvents.Add<Position>:
                    Console.WriteLine("Position added!");
                    break;
                case WorldEvents.Remove<Position>:
                    Console.WriteLine("Position removed!");
                    break;

                case Position.SetX:
                    Console.WriteLine("Set X!");
                    Console.WriteLine("\tPrevious X: " + snapshot.X);
                    break;
                case Position.SetY:
                    Console.WriteLine("Set Y!");
                    Console.WriteLine("\tPrevious Y: " + snapshot.Y);
                    break;
            }
        }
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<TestTemplateEventSystem>()
            .Add<TestEventSystem>()
            .Add<TestSnapshotEventSystem>()
            .RegisterTo(world, scheduler);

        var e = world.CreateInArrayHost(HList.Create(new Position(1, 1)));
        var pos = new Position.View(e);

        e.Send(new Position.MoveUp(5));
        e.Send(new Position.MoveLeft(5));
        scheduler.Tick();

        e.Modify(new Position.TestCommand());
        scheduler.Tick();

        pos.X += 10;
        scheduler.Tick();

        pos.Y += 5;
        scheduler.Tick();

        e.Remove<Position>();
        scheduler.Tick();
    }
}