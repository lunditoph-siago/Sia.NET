namespace Sia.Tests.Auxiliary;

public class ContextTests
{
    [Fact]
    public async Task ContextValue_Assign_Test()
    {
        // Arrange
        const string valueForThread1 = "ThreadValue1";
        const string valueForThread2 = "ThreadValue2";
        var result1 = string.Empty;
        var result2 = string.Empty;

        Context<string>.Current = "The best Sia";

        var task1 = Task.Run(() =>
        {
            Context<string>.Current = valueForThread1;
            result1 = Context.Get<string>();
        });

        var task2 = Task.Run(() =>
        {
            Context<string>.Current = valueForThread2;
            result2 = Context.Get<string>();
        });

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.Throws<NotSupportedException>(Context.Get<string>); // TODO: Assign value in thread will override the data in main thread.
        Assert.Equal(valueForThread1, result1);
        Assert.Equal(valueForThread2, result2);

        // Clean-up
        Context<string>.Current = null;
    }

    [Fact]
    public async Task ContextValue_WithMethod_Test()
    {
        // Arrange
        const string valueForThread1 = "ThreadValue1";
        const string valueForThread2 = "ThreadValue2";
        var result1 = string.Empty;
        var result2 = string.Empty;

        Context<string>.Current = "The best Sia";

        var task1 = Task.Run(() =>
        {
            Context<string>.With(valueForThread1, () =>
            {
                result1 = Context.Get<string>();
            });
        });

        var task2 = Task.Run(() =>
        {
            Context<string>.With(valueForThread2, () =>
            {
                result2 = Context.Get<string>();
            });
        });

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.Throws<NotSupportedException>(Context.Get<string>); // TODO: Assign value in thread will override the data in main thread.
        Assert.Equal(valueForThread1, result1);
        Assert.Equal(valueForThread2, result2);

        // Clean-up
        Context<string>.Current = null;
    }
}