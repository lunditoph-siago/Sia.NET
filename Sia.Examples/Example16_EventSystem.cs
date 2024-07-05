namespace Sia_Examples;

using System.Collections.Immutable;
using Sia;

public static partial class Example16_EventSystem
{
    [SiaTemplate(nameof(Position))]
    public record RPosition(int X, int Y);

    public partial record struct Position
    {
        public readonly record struct TestCommand : ICommand
        {
            public void Execute(World world, Entity target)
                => target.Get<Position>().X++;
        }
    }

    [SiaTemplate(nameof(Tags))]
    public record RTags
    {
        [Sia(Item = "")]
        public ImmutableArray<string> List { get; init; } = [];
    }

    [SiaEvents]
    public partial class MoveEvents
    {
        public readonly record struct MoveUp(int Count) : IEvent;
        public readonly record struct MoveDown(int Count) : IEvent;
        public readonly record struct MoveLeft(int Count) : IEvent;
        public readonly record struct MoveRight(int Count) : IEvent;
    }

    public class TestEventSystem : EventSystemBase
    {
        public override void Initialize(World world, Scheduler scheduler)
        {
            base.Initialize(world, scheduler);
            RecordEvents<MoveEvents>();
        }

        protected override void HandleEvent<TEvent>(Entity e, in TEvent @event)
        {
            static Position.View Get(in Entity e)
                => new(e);

            switch (@event) {
                case MoveEvents.MoveUp(int count):
                    Console.WriteLine("Move Up!");
                    Get(e).Y += count;
                    break;
                case MoveEvents.MoveDown(int count):
                    Console.WriteLine("Move Down!");
                    Get(e).Y -= count;
                    break;
                case MoveEvents.MoveLeft(int count):
                    Console.WriteLine("Move Left!");
                    Get(e).X -= count;
                    break;
                case MoveEvents.MoveRight(int count):
                    Console.WriteLine("Move Right!");
                    Get(e).X += count;
                    break;
            }
        }
    }

    public class TestSnapshotEventSystem : SnapshotEventSystemBase<Position>
    {
        public override void Initialize(World world, Scheduler scheduler)
        {
            base.Initialize(world, scheduler);
            RecordFor<Position>();
            RecordEvent<Position.TestCommand>();
        }

        protected override Position Snapshot<TEvent>(Entity entity, in TEvent e)
            => entity.Get<Position>();

        protected override void HandleEvent<TEvent>(
            Entity e, in Position snapshot, in TEvent @event)
        {
            switch (@event) {
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
            Entity entity, in Position snapshot, in TEvent e)
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

    public class TagsTemplateEventSystem : TemplateEventSystemBase<Tags, RTags>
    {
        protected override void HandleEvent<TEvent>(Entity entity, in Tags snapshot, in TEvent e)
        {
            switch (e) {
                case Tags.Add(string tag):
                    Console.WriteLine("Tag added: " + tag);
                    Console.WriteLine("\tPrevious tags: " + string.Join(", ", snapshot.List));
                    break;
                case Tags.Remove(string tag):
                    Console.WriteLine("Tag removed: " + tag);
                    Console.WriteLine("\tPrevious tags: " + string.Join(", ", snapshot.List));
                    break;
                case Tags.Set(int index, string tag):
                    Console.WriteLine("Tag set: " + tag + ", index: " + index);
                    Console.WriteLine("\tPrevious tags: " + string.Join(", ", snapshot.List));
                    break;
                case Tags.SetList(var list):
                    Console.WriteLine("Tags changed: " + string.Join(", ", list));
                    Console.WriteLine("\tPrevious tags: " + string.Join(", ", snapshot.List));
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
            .Add<TagsTemplateEventSystem>()
            .RegisterTo(world, scheduler);

        var e = world.CreateInArrayHost(HList.Create(
            new Position(1, 1), new Tags([])));

        var pos = new Position.View(e);
        var tags = new Tags.View(e);

        e.Send(new MoveEvents.MoveUp(5));
        e.Send(new MoveEvents.MoveLeft(5));
        tags.Add("Test");
        scheduler.Tick();

        e.Execute(new Position.TestCommand());
        tags.Remove("Test");
        scheduler.Tick();

        pos.X += 10;
        tags.List = ["a", "b", "c"];
        tags.Set(1, "d");
        scheduler.Tick();

        pos.Y += 5;
        tags.List = [];
        scheduler.Tick();

        e.Remove<Position>();
        scheduler.Tick();
    }
}