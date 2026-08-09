namespace Sia_BrowserTest.Acceptance;

public interface IAcceptanceStage
{
    public string Name { get; }

    public Task RunAsync(AcceptanceContext context);
}
