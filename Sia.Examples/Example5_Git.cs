namespace Sia_Examples;

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using MemoryPack;
using MemoryPack.Compression;
using Sia;
using Sia.Serialization;
using Sia.Serialization.Binary;

[MemoryPackable]
public partial record struct Author(string Name);

[MemoryPackable]
public partial record struct Parent(EntityId Commit);

public static partial class Example5_Git
{
    [SiaTemplate(nameof(FileStat))]
    public record RFileStat(int Lines, int Bytes);

    public partial record struct FileStat
    {
        public readonly record struct Touch(int LinesAdded) : ICommand
        {
            public void Execute(World world, Entity target)
                => target.Get<FileStat>().Lines += LinesAdded;
        }
    }

    [SiaTemplate(nameof(Refs))]
    public record RRefs
    {
        [Sia(Item = "")]
        public ImmutableArray<string> Names { get; init; } = [];
    }

    [SiaEvents]
    public partial class GitEvents
    {
        public readonly record struct Commit(string Message) : IRevertableEvent;
        public readonly record struct Checkout(string Branch) : IEvent;
        public readonly record struct Merge(string Branch) : IEvent;
        public readonly record struct Rebase(string Onto) : IPausableEvent;
    }

    public readonly record struct CommitRecord(GitEvents.Commit Commit, int LinesBefore) : IEvent;

    public static class Blob
    {
        public static Entity Root(World world, string path, int lines, int bytes)
            => world.Create(HList.From(
                new Author(path),
                new FileStat(lines, bytes),
                new Refs([])));

        public static Entity Child(World world, string path, int lines, int bytes, Entity parent)
            => world.Create(HList.From(
                new Author(path),
                new FileStat(lines, bytes),
                new Refs([]),
                new Parent(parent.Id)));
    }

    public class CommitLogSystem : EventSystemBase
    {
        public override void Initialize(World world)
        {
            base.Initialize(world);
            RecordEvents<GitEvents>();
            RecordEvent<HOEvents.Revert<GitEvents.Commit>>();
        }

        protected override void HandleEvent<TEvent>(Entity e, in TEvent @event)
        {
            switch (@event) {
                case GitEvents.Commit(string message):
                    Console.WriteLine($"git log: commit -m \"{message}\"");
                    break;
                case GitEvents.Checkout(string branch):
                    Console.WriteLine($"git log: checkout {branch}");
                    break;
                case GitEvents.Merge(string branch):
                    Console.WriteLine($"git log: merge {branch}");
                    break;
                case GitEvents.Rebase(string onto):
                    Console.WriteLine($"git log: rebase --onto {onto}");
                    break;
                case HOEvents.Revert<GitEvents.Commit> revert:
                    Console.WriteLine($"git log: revert \"{revert.Value.Message}\"");
                    break;
            }
        }
    }

    public sealed class CommitHistorySystem : SnapshotEventSystemBase<int>
    {
        public readonly InfiniteHistory<Entity, CommitRecord> History = new();

        public override void Initialize(World world)
        {
            base.Initialize(world);
            RecordEvent<GitEvents.Commit>();
        }

        public override void Uninitialize(World world)
        {
            base.Uninitialize(world);
            History.Dispose();
        }

        protected override int Snapshot<TEvent>(Entity entity, in TEvent e)
            => entity.Get<FileStat>().Lines;

        protected override void HandleEvent<TEvent>(Entity entity, in int linesBefore, in TEvent @event)
        {
            if (@event is GitEvents.Commit commit) {
                History.Record(entity, new CommitRecord(commit, linesBefore));
            }
        }
    }

    public sealed class RebaseStepSystem() : SystemBase(
        Matchers.Of<FileStat>(),
        trigger: EventUnion.Of<GitEvents.Rebase, HOEvents.Resume<GitEvents.Rebase>>(),
        filter: EventUnion.Of<HOEvents.Pause<GitEvents.Rebase>>())
    {
        public override void Execute(World world, IEntityQuery query)
            => Console.WriteLine($"  rebase step applied to {query.Count} blob(s)");
    }

    public class DiffSystem : SnapshotEventSystemBase<FileStat>
    {
        public override void Initialize(World world)
        {
            base.Initialize(world);
            RecordFor<FileStat>();
            RecordEvent<FileStat.Touch>();
        }

        protected override FileStat Snapshot<TEvent>(Entity entity, in TEvent e)
            => entity.Get<FileStat>();

        protected override void HandleEvent<TEvent>(Entity e, in FileStat snapshot, in TEvent @event)
        {
            switch (@event) {
                case FileStat.Touch:
                    var current = e.Get<FileStat>();
                    Console.WriteLine($"git diff: lines {snapshot.Lines} -> {current.Lines}");
                    break;
            }
        }
    }

    public class FileStatHistorySystem : TemplateEventSystemBase<FileStat, RFileStat>
    {
        protected override void HandleEvent<TEvent>(Entity entity, in FileStat snapshot, in TEvent e)
        {
            switch (e) {
                case WorldEvents.Add<FileStat>:
                    Console.WriteLine("reflog: blob created");
                    break;
                case WorldEvents.Remove<FileStat>:
                    Console.WriteLine("reflog: blob removed");
                    break;
                case FileStat.SetLines:
                    Console.WriteLine($"reflog: lines changed (was {snapshot.Lines})");
                    break;
                case FileStat.SetBytes:
                    Console.WriteLine($"reflog: bytes changed (was {snapshot.Bytes})");
                    break;
            }
        }
    }

    public class RefsHistorySystem : TemplateEventSystemBase<Refs, RRefs>
    {
        protected override void HandleEvent<TEvent>(Entity entity, in Refs snapshot, in TEvent e)
        {
            var was = string.Join(", ", snapshot.Names);
            switch (e) {
                case Refs.Add(string name):
                    Console.WriteLine($"reflog: branch '{name}' created (was: [{was}])");
                    break;
                case Refs.Remove(string name):
                    Console.WriteLine($"reflog: branch '{name}' deleted (was: [{was}])");
                    break;
                case Refs.Set(int index, string name):
                    Console.WriteLine($"reflog: ref[{index}] moved to '{name}' (was: [{was}])");
                    break;
                case Refs.SetNames(var list):
                    Console.WriteLine($"reflog: refs replaced with [{string.Join(", ", list)}] (was: [{was}])");
                    break;
            }
        }
    }

    public static void RunHistory(World world)
    {
        var commitHistory = new CommitHistorySystem();

        using var stage = SystemChain.Empty
            .Add<CommitLogSystem>()
            .Add<DiffSystem>()
            .Add<FileStatHistorySystem>()
            .Add<RefsHistorySystem>()
            .Add<CommitHistorySystem>(() => commitHistory)
            .Add<RebaseStepSystem>()
            .CreateStage(world);

        Console.WriteLine("-- Scene 1: a tiny repository --");

        var readme = Blob.Root(world, "README.md", lines: 10, bytes: 200);
        var readmeRefs = new Refs.View(readme, world);
        var readmeStat = new FileStat.View(readme, world);

        readme.Send(new GitEvents.Commit("initial commit"));
        readmeRefs.Add("main");
        stage.Tick();

        readme.Execute(new FileStat.Touch(5));
        readme.Send(new GitEvents.Commit("docs: expand usage section"));
        stage.Tick();

        readmeRefs.Add("release/1.0");
        readme.Send(new GitEvents.Checkout("release/1.0"));
        stage.Tick();

        readmeStat.Lines += 3;
        readme.Send(new GitEvents.Merge("main"));
        readmeRefs.Names = ["main", "release/1.0", "release/1.1"];
        stage.Tick();

        readmeRefs.Remove("release/1.0");
        readme.Send(new GitEvents.Rebase("main"));
        stage.Tick();

        var changelog = Blob.Child(world, "CHANGELOG.md", lines: 3, bytes: 40, parent: readme);
        changelog.Send(new GitEvents.Commit("chore: add changelog"));
        stage.Tick();

        Console.WriteLine();
        Console.WriteLine("-- Scene 2: git revert HEAD (HOEvents.Revert + History) --");

        Console.WriteLine("reflog of README.md's commits:");
        foreach (var record in commitHistory.History) {
            if (record.Target != readme) {
                continue;
            }
            Console.WriteLine($"  \"{record.Event.Commit.Message}\" (lines before: {record.Event.LinesBefore})");
        }

        var lastReadmeCommit = commitHistory.History.Last(r => r.Target == readme);
        var revertedCommit = lastReadmeCommit.Event.Commit;
        Console.WriteLine($"git revert HEAD  # undoing \"{revertedCommit.Message}\"");
        readmeStat.Lines = lastReadmeCommit.Event.LinesBefore;
        readme.Send(new HOEvents.Revert<GitEvents.Commit>(revertedCommit));
        stage.Tick();

        Console.WriteLine();
        Console.WriteLine("-- Scene 3: git rebase --continue, and a Pause/Resume gotcha --");

        readme.Send(new GitEvents.Rebase("develop"));
        readme.Send(HOEvents.Pause<GitEvents.Rebase>.Instance);
        stage.Tick();
        Console.WriteLine("rebase paused (conflict in README.md); no \"rebase step applied\" above == correct");

        Console.WriteLine("git rebase --continue");
        readme.Send(HOEvents.Resume<GitEvents.Rebase>.Instance);
        stage.Tick();
        Console.WriteLine("^ that line printed even though we only sent Resume, not a fresh Rebase --");
        Console.WriteLine("  RebaseStepSystem uses plain SystemBase(trigger:, filter:), which only knows");
        Console.WriteLine("  \"this entity is due to run this tick\", not which event caused it. Pause/Resume");
        Console.WriteLine("  aren't a persistent on/off switch here -- for that, use EventSystemBase instead,");
        Console.WriteLine("  which dispatches per event type (see CommitLogSystem/CommitHistorySystem above).");

        world.Create(HList.From(42, "loose-object", 3.14f, Math.PI));
    }

    private sealed class GitHostHeaderSerializer : IHostHeaderSerializer
    {
        private const byte RootBlobTag = 1;
        private const byte ChildBlobTag = 2;
        private const byte LooseObjectTag = 3;

        [UnconditionalSuppressMessage("Trimming", "IL2046",
            Justification = "This implementation never resolves types by name; it only matches a fixed, compile-time-known set of entity shapes.")]
        [UnconditionalSuppressMessage("AOT", "IL3051",
            Justification = "This implementation never constructs generic types/methods at runtime; every type reference is a concrete closed generic.")]
        public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
            where TBufferWriter : IBufferWriter<byte>
        {
            var tag = host switch {
                WorldEntityHost<HList<Author, HList<FileStat, HList<Refs, EmptyHList>>>,
                    ArrayEntityHost<HList<Author, HList<FileStat, HList<Refs, EmptyHList>>>>> => RootBlobTag,
                WorldEntityHost<HList<Author, HList<FileStat, HList<Refs, HList<Parent, EmptyHList>>>>,
                    ArrayEntityHost<HList<Author, HList<FileStat, HList<Refs, HList<Parent, EmptyHList>>>>>> => ChildBlobTag,
                WorldEntityHost<HList<int, HList<string, HList<float, HList<double, EmptyHList>>>>,
                    ArrayEntityHost<HList<int, HList<string, HList<float, HList<double, EmptyHList>>>>>> => LooseObjectTag,
                _ => throw new NotSupportedException(
                    $"GitHostHeaderSerializer doesn't know this entity shape: {host.GetType()}")
            };

            var span = writer.GetSpan(1);
            span[0] = tag;
            writer.Advance(1);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2046",
            Justification = "This implementation never resolves types by name; it only matches a fixed, compile-time-known set of entity shapes.")]
        [UnconditionalSuppressMessage("AOT", "IL3051",
            Justification = "This implementation never constructs generic types/methods at runtime; every type reference is a concrete closed generic.")]
        public static IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world)
        {
            if (buffer.Length == 0) {
                return null;
            }

            var reader = new SequenceReader<byte>(buffer);
            reader.TryRead(out var tag);
            buffer = reader.UnreadSequence;

            return tag switch {
                RootBlobTag =>
                    world.AcquireHost<HList<Author, HList<FileStat, HList<Refs, EmptyHList>>>,
                        ArrayEntityHost<HList<Author, HList<FileStat, HList<Refs, EmptyHList>>>>>(),
                ChildBlobTag =>
                    world.AcquireHost<HList<Author, HList<FileStat, HList<Refs, HList<Parent, EmptyHList>>>>,
                        ArrayEntityHost<HList<Author, HList<FileStat, HList<Refs, HList<Parent, EmptyHList>>>>>>(),
                LooseObjectTag =>
                    world.AcquireHost<HList<int, HList<string, HList<float, HList<double, EmptyHList>>>>,
                        ArrayEntityHost<HList<int, HList<string, HList<float, HList<double, EmptyHList>>>>>>(),
                _ => throw new NotSupportedException($"Unknown host tag: {tag}")
            };
        }
    }

    private sealed class FileStatFormatter : MemoryPackFormatter<FileStat>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer, scoped ref FileStat value)
        {
            writer.WriteValue(value.Lines);
            writer.WriteValue(value.Bytes);
        }

        public override void Deserialize(ref MemoryPackReader reader, scoped ref FileStat value)
            => value = new FileStat(reader.ReadValue<int>(), reader.ReadValue<int>());
    }

    private sealed class RefsFormatter : MemoryPackFormatter<Refs>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer, scoped ref Refs value)
            => writer.WriteValue(value.Names);

        public override void Deserialize(ref MemoryPackReader reader, scoped ref Refs value)
            => value = new Refs(reader.ReadValue<ImmutableArray<string>>());
    }

    private static class GitSerializer
    {
        static GitSerializer()
        {
            MemoryPackFormatterProvider.Register(new FileStatFormatter());
            MemoryPackFormatterProvider.Register(new RefsFormatter());
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "GitHostHeaderSerializer, the THostHeaderSerializer used here, never resolves types by name.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "GitHostHeaderSerializer, the THostHeaderSerializer used here, never constructs generics at runtime.")]
        public static void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
            where TBufferWriter : IBufferWriter<byte>
            => BinaryWorldSerializer<GitHostHeaderSerializer, MemoryPackComponentSerializer>
                .Serialize(ref writer, world);

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "GitHostHeaderSerializer, the THostHeaderSerializer used here, never resolves types by name.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "GitHostHeaderSerializer, the THostHeaderSerializer used here, never constructs generics at runtime.")]
        public static void Deserialize(ref ReadOnlySequence<byte> buffer, World world)
            => BinaryWorldSerializer<GitHostHeaderSerializer, MemoryPackComponentSerializer>
                .Deserialize(ref buffer, world);
    }

    public static void RunPackAndClone(World world)
    {
        Console.WriteLine();
        Console.WriteLine("-- Scene 4: git gc (pack objects), then clone --");

#if BROWSER
        var buffer = new ArrayBufferWriter<byte>();
        GitSerializer.Serialize(ref buffer, world);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        Console.WriteLine("packed objects:");
        Console.WriteLine(Encoding.Unicode.GetString(seq));

        Console.WriteLine();
        var clone = new World();
        GitSerializer.Deserialize(ref seq, clone);
#else
        var compressor = new BrotliCompressor();
        GitSerializer.Serialize(ref compressor, world);

        var seq = new ReadOnlySequence<byte>(compressor.ToArray());
        var packedLength = seq.Length;
        Console.WriteLine("packed objects (git packs with zlib; we pack with brotli here):");
        Console.WriteLine(Encoding.Unicode.GetString(seq));

        var decompressor = new BrotliDecompressor();
        seq = decompressor.Decompress(seq);
        var unpackedLength = seq.Length;

        Console.WriteLine();
        Console.WriteLine("unpacked:");
        Console.WriteLine(Encoding.Unicode.GetString(seq));

        Console.WriteLine();
        Console.WriteLine("pack ratio: " + (float)packedLength / unpackedLength);

        var clone = new World();
        GitSerializer.Deserialize(ref seq, clone);
#endif

        Console.WriteLine();
        Console.WriteLine("git clone result:");
        foreach (var e in clone) {
            Console.WriteLine(e + ": " + e.Boxed);
        }
    }

    public static void Run(World world)
    {
        RunHistory(world);
        RunPackAndClone(world);
    }
}
