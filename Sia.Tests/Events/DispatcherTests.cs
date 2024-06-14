namespace Sia.Tests.Events;

public class DispatcherTests
{
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
}