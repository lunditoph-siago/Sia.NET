using Sia.Examples.Editor;

namespace Sia.Examples.Tests;

public class EditorShaderJobsTests
{
    private static ShaderRequest Request(string folder) => new(ShaderAction.Run, folder, null);

    [Fact]
    public void StopDiscardsQueuedRunAndInvalidatesTheActiveResult()
    {
        var jobs = new EditorShaderJobs();
        var active = jobs.Start(Request("first"), default);
        var token = active.Token;
        Assert.False(jobs.IsCancellationRequested);
        jobs.Queue(Request("queued"));
        Assert.True(jobs.IsCancellationRequested);

        jobs.Cancel();

        Assert.True(token.IsCancellationRequested);
        Assert.False(jobs.IsCurrent(active.Revision));
        Assert.True(jobs.IsRunning); // The compiler phase still owns the token.
        Assert.Null(jobs.Complete(active));
        Assert.False(jobs.IsRunning);
    }

    [Fact]
    public void RepeatedRequestsRunOnlyTheLatestAfterTheActivePhaseCompletes()
    {
        var jobs = new EditorShaderJobs();
        var active = jobs.Start(Request("first"), default);
        jobs.Queue(Request("superseded"));
        var latest = Request("latest");
        jobs.Queue(latest);

        Assert.Throws<InvalidOperationException>(() => jobs.Start(latest, default));
        Assert.Same(latest, jobs.Complete(active));
        var next = jobs.Start(latest, default);
        Assert.False(jobs.IsCancellationRequested);
        Assert.True(jobs.IsCurrent(next.Revision));
        Assert.False(jobs.IsCurrent(active.Revision));
        Assert.False(next.Token.IsCancellationRequested);
        Assert.Null(jobs.Complete(next));
    }

    [Fact]
    public void ClosingThePagePreventsAQueuedRestart()
    {
        using var lifetime = new CancellationTokenSource();
        var jobs = new EditorShaderJobs();
        var active = jobs.Start(Request("first"), lifetime.Token);
        jobs.Queue(Request("queued"));

        lifetime.Cancel();

        Assert.Null(jobs.Complete(active));
        Assert.False(jobs.IsRunning);
    }

    [Fact]
    public void CompletionOfAnOldJobCannotClearTheNewJob()
    {
        var jobs = new EditorShaderJobs();
        var first = jobs.Start(Request("first"), default);
        jobs.Complete(first);
        var next = jobs.Start(Request("next"), default);

        Assert.Throws<InvalidOperationException>(() => jobs.Complete(first));
        Assert.True(jobs.IsRunning);
        Assert.True(jobs.IsCurrent(next.Revision));
        jobs.Complete(next);
    }

    [Fact]
    public void SourceInvalidationKeepsTheLatestQueuedIntentUntilExplicitCancellation()
    {
        var jobs = new EditorShaderJobs();
        var active = jobs.Start(Request("first"), default);
        var latest = Request("latest");
        jobs.Queue(latest);

        jobs.Cancel(clearPending: false);

        Assert.False(jobs.IsCurrent(active.Revision));
        Assert.Same(latest, jobs.Complete(active));
    }
}
