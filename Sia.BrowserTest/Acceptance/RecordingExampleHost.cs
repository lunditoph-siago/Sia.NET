using Sia_Examples;

namespace Sia_BrowserTest.Acceptance;

public sealed class RecordingExampleHost : IRenderHost<ExampleItemView>
{
    public List<ExampleItemView> Upserts { get; } = [];

    public List<ExampleItemView> Removals { get; } = [];

    public void Clear()
    {
        Upserts.Clear();
        Removals.Clear();
    }

    public void Upsert(in ExampleItemView view) => Upserts.Add(view);

    public void Remove(in ExampleItemView view) => Removals.Add(view);
}
