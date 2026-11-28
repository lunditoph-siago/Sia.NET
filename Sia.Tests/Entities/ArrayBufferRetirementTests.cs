using System.Buffers;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Row = Sia.HList<Sia.Tests.Entities.ArrayBufferRetirementTests.Component, Sia.EmptyHList>;

namespace Sia.Tests.Entities;

public sealed class ArrayBufferRetirementTests
{
    private const int RentalLength = 1_024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    internal readonly record struct Component(int Value);
    private readonly record struct AddedComponent(int Value);

    [Fact]
    public void Dispose_AllowsAnotherThreadToReadDuringThePoolReturnNotification()
    {
        using var world = new World();
        var (host, _, backing) = CreateRentedRows(world);
        using var ready = new ManualResetEventSlim();
        using var readRequested = new ManualResetEventSlim();
        using var readStarted = new ManualResetEventSlim();
        using var readFinished = new ManualResetEventSlim();
        var readValue = new Component(-1);
        Exception? readError = null;
        var reader = new Thread(() => {
            ready.Set();
            if (!readRequested.Wait(Timeout)) return;
            readStarted.Set();
            try {
                readValue = host.GetRef(0).Head;
            }
            catch (Exception error) {
                readError = error;
            }
            finally {
                readFinished.Set();
            }
        }) { IsBackground = true };

        var readCompletedDuringReturn = false;
        using var listener = new ArrayReturnListener(backing, () => {
            readRequested.Set();
            readCompletedDuringReturn = readStarted.Wait(Timeout) && readFinished.Wait(Timeout);
        });
        reader.Start();
        try {
            Assert.True(ready.Wait(Timeout));
            world.Dispose();
        }
        finally {
            readRequested.Set();
            Assert.True(reader.Join(Timeout));
        }

        Assert.Equal(1, listener.ReturnCount);
        Assert.True(readCompletedDuringReturn);
        Assert.Null(readError);
        Assert.Equal(default, readValue);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(RentalLength)]
    public void InvalidRefsAfterDisposalLeaveTheBackingDetached(int index)
    {
        using var world = new World();
        var (host, _, backing) = CreateRentedRows(world);
        world.Dispose();
        var buffer = host.InnerHost.Buffer;
        Assert.Empty(GetBacking(buffer));

        Assert.True(Unsafe.IsNullRef(ref buffer.GetRefOrNullRef(index)));
        Assert.Empty(GetBacking(buffer));
        Assert.Throws<IndexOutOfRangeException>(() => buffer.GetRef(index));
        Assert.Empty(GetBacking(buffer));

        Assert.Equal(default, buffer.GetRef(0));
        var restored = GetBacking(buffer);
        Assert.Equal(RentalLength, restored.Length);
        Assert.NotSame(backing, restored);
    }

    [Theory]
    [InlineData(nameof(Entity.Boxed))]
    [InlineData(nameof(Entity.Set))]
    [InlineData(nameof(Entity.Add))]
    [InlineData(nameof(Entity.AddMany))]
    [InlineData(nameof(Entity.Remove))]
    [InlineData(nameof(Entity.RemoveMany))]
    [InlineData(nameof(Entity.Destroy))]
    [InlineData(nameof(World.ReleaseHost))]
    public void ScalarOperationsAllowDeterministicArrayReturn(string operation)
    {
        using var world = new World();
        var (host, entity, backing) = CreateRentedRows(world);
        using var listener = new ArrayReturnListener(backing);

        switch (operation) {
            case nameof(Entity.Boxed):
                Assert.Equal(new Component(7), Assert.IsType<Row>(entity.Boxed).Head);
                break;
            case nameof(Entity.Set):
                entity.Set(new Component(11));
                Assert.Equal(new Component(11), backing[0].Head);
                break;
            case nameof(Entity.Add):
                var addedHost = world.GetArrayHost<HList<AddedComponent, Row>>();
                entity.Add(new AddedComponent(13));
                Assert.Same(addedHost, entity.Host);
                break;
            case nameof(Entity.AddMany):
                var appendedHost = world.GetArrayHost<HList<Component, HList<AddedComponent, EmptyHList>>>();
                entity.AddMany(HList.From(new AddedComponent(13)));
                Assert.Same(appendedHost, entity.Host);
                break;
            case nameof(Entity.Remove):
            case nameof(Entity.RemoveMany):
                var emptyHost = world.GetArrayHost<EmptyHList>();
                if (operation == nameof(Entity.Remove)) entity.Remove<Component>();
                else entity.RemoveMany<Row>();
                Assert.Same(emptyHost, entity.Host);
                break;
            case nameof(Entity.Destroy):
                entity.Destroy();
                Assert.False(entity.IsValid);
                break;
            case nameof(World.ReleaseHost):
                Assert.True(world.ReleaseHost<WorldEntityHost<Row, ArrayEntityHost<Row>>>());
                Assert.False(entity.IsValid);
                Assert.Equal(1, listener.ReturnCount);
                break;
        }

        world.Dispose();

        Assert.Equal(0, host.Count);
        Assert.Equal(1, listener.ReturnCount);
    }

    [Fact]
    public void EscapedRefKeepsItsBackingWhenTheWorldIsDisposed()
    {
        using var world = new World();
        var (host, _, backing) = CreateRentedRows(world);
        using var listener = new ArrayReturnListener(backing);
        ref var row = ref host.GetRef(0);

        world.Dispose();

        Assert.Equal(0, listener.ReturnCount);
        Assert.Equal(default, row);
        row.Head = new Component(17);
        Assert.True(Unsafe.AreSame(ref row, ref host.GetRef(0)));
        Assert.Equal(new Component(17), host.GetRef(0).Head);
    }

    private static (WorldEntityHost<Row, ArrayEntityHost<Row>> Host, Entity First, Row[] Backing) CreateRentedRows(World world)
    {
        var host = world.GetArrayHost<Row>();
        var first = host.Create(HList.From(new Component(7)));
        for (var i = 0; i < RentalLength / 2; i++) host.Create(default);
        // The public Buffer getter exposes the storage and changes its retirement policy.
        var storage = (ArrayBuffer<Row>)typeof(BufferEntityHost<Row, ArrayBuffer<Row>>)
            .GetProperty("Storage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host.InnerHost)!;
        var backing = GetBacking(storage);
        Assert.Equal(RentalLength, backing.Length);
        return (host, first, backing);
    }

    private static Row[] GetBacking(ArrayBuffer<Row> buffer)
        => (Row[])typeof(ArrayBuffer<Row>).GetField("_array", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(buffer)!;

    private sealed class ArrayReturnListener(Row[] backing, Action? onReturn = null) : EventListener
    {
        private const string ArrayPoolProvider = "System.Buffers.ArrayPoolEventSource";
        private const int BufferReturned = 3;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly int _bufferId = backing.GetHashCode();
        private readonly int _bufferLength = backing.Length;
        private readonly int _poolId = ArrayPool<Row>.Shared.GetHashCode();

        public int ReturnCount { get; private set; }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == ArrayPoolProvider) EnableEvents(source, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs trace)
        {
            if (Environment.CurrentManagedThreadId != _thread || trace.EventSource.Name != ArrayPoolProvider
                || trace.EventId != BufferReturned || trace.Payload is not { Count: >= 3 } payload
                || payload[0] is not int bufferId || bufferId != _bufferId
                || payload[1] is not int size || size != _bufferLength
                || payload[2] is not int poolId || poolId != _poolId) return;
            ReturnCount++;
            onReturn?.Invoke();
        }
    }
}
