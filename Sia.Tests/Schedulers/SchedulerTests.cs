namespace Sia.Tests.Schedulers;

public class SchedulerTests
{
    [Fact]
    public void Scheduler_Task_Test()
    {
        Assert.Multiple(
            () => {
                // Arrange
                var scheduler = new Scheduler();
                var counter = 0;
                scheduler.CreateTask(() => {
                    counter++;
                    return true;
                });

                // Act
                scheduler.Tick();
                scheduler.Tick();

                // Assert
                Assert.Equal(1, counter);
            },
            () => {
                // Arrange
                var scheduler = new Scheduler();
                var counter = 0;
                const int execute = 12;
                scheduler.CreateTask(() => {
                    counter++;
                    return false;
                });

                // Act
                for (var i = 0; i < execute; i++) scheduler.Tick();

                // Assert
                Assert.Equal(execute, counter);
            });
    }

    [Fact]
    public void Scheduler_Dependencies_Test()
    {
        // Arrange
        var scheduler = new Scheduler();
        var counter = 0;
        var mainTask = scheduler.CreateTask(() => {
            counter++;
            return false;
        });
        scheduler.CreateTask(() => {
            counter++;
            return true;
        }, [mainTask]);

        // Act
        scheduler.Tick();

        // Assert
        Assert.Equal(2, counter);
    }
}