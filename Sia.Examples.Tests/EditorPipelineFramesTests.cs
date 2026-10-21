using Sia.Examples.Editor;

namespace Sia.Examples.Tests;

public class EditorPipelineFramesTests
{
    [Fact]
    public void InitializationAndWaitingFramesRetainInputInOrderUntilRendering()
    {
        var frames = new EditorPipelineFrames();
        frames.Receive("[10,200,100,1,6,0,0,65,1]"); // Key down during adapter setup.
        frames.Receive("[20,300,150,2]"); // Paused or readback still pending.
        frames.Receive("[30,400,200,2,8,0,0,65,0]"); // Key up before rendering.

        Assert.Equal(30, frames.Time);
        Assert.Equal(400, frames.CssWidth);
        Assert.Equal(200, frames.CssHeight);
        Assert.Equal(2, frames.Scale);
        Assert.Equal(new EditorPipelineInput[] {
            new(EditorPipelineInputKind.KeyPressed, 0, 0, 65, 1),
            new(EditorPipelineInputKind.KeyReleased, 0, 0, 65, 0),
        }, frames.Inputs.ToArray());

        frames.ClearInputs();
        frames.Receive("[40,400,200,2]");
        Assert.Empty(frames.Inputs.ToArray());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[10,200,100]")]
    [InlineData("[10,200,100,1,6]")]
    public void MalformedPacketsCannotPartiallyReplaceTheLastFrame(string packet)
    {
        var frames = new EditorPipelineFrames();
        frames.Receive("[10,200,100,1,6,0,0,65,0]");

        Assert.Throws<InvalidDataException>(() => frames.Receive(packet));

        Assert.Equal(200, frames.CssWidth);
        Assert.Equal(new EditorPipelineInput[] { new(EditorPipelineInputKind.KeyPressed, 0, 0, 65, 0) }, frames.Inputs.ToArray());
    }
}
