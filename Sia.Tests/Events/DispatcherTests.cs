namespace Sia.Tests.Events;

public class DispatcherTests
{
    private sealed class CountingListener : IEventListener<int>
    {
        public int Count { get; private set; }

        public bool OnEvent<TEvent>(int target, in TEvent e)
            where TEvent : IEvent
        {
            Count++;
            return false;
        }
    }

    public readonly record struct AssertCommand(int Expected) : ICommand
    {
        public void Execute(World world, Entity target) { }
    }

    [Fact]
    public void Dispatcher_Test()
    {
        var dispatcher = new Dispatcher<int, IEvent>();
        dispatcher.Listen((int target, in AssertCommand e) => {
            Assert.Equal(e.Expected, target);
            return false;
        });

        dispatcher.Send(1, new AssertCommand(1));
        dispatcher.Send(2, new AssertCommand(2));
    }

    [Fact]
    public void UnlistenTarget_DifferentListener_DoesNotRemoveRegisteredListener()
    {
        var dispatcher = new Dispatcher<int, IEvent>();
        var registered = new CountingListener();
        var other = new CountingListener();
        dispatcher.Listen(1, registered);

        Assert.False(dispatcher.Unlisten(1, other));

        dispatcher.Send(1, new AssertCommand(1));
        Assert.Equal(1, registered.Count);
    }

    [Fact]
    public void UnlistenTarget_LastListener_DoesNotLeakAcrossTargets()
    {
        var dispatcher = new Dispatcher<int, IEvent>();
        var first = new CountingListener();
        var second = new CountingListener();
        dispatcher.Listen(1, first);

        Assert.True(dispatcher.Unlisten(1, first));
        dispatcher.Listen(2, second);

        dispatcher.Send(1, new AssertCommand(1));
        dispatcher.Send(2, new AssertCommand(2));
        Assert.Equal(0, first.Count);
        Assert.Equal(1, second.Count);
    }

    [Fact]
    public void UnlistenAllTarget_ClearsPooledListenerList()
    {
        var dispatcher = new Dispatcher<int, IEvent>();
        var first = new CountingListener();
        var second = new CountingListener();
        dispatcher.Listen(1, first);

        dispatcher.UnlistenAll(1);
        dispatcher.Listen(2, second);

        dispatcher.Send(2, new AssertCommand(2));
        Assert.Equal(0, first.Count);
        Assert.Equal(1, second.Count);
    }
}
