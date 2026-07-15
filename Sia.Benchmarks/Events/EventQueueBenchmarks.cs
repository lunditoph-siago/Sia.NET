using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Events", "EventQueue", "EndToEnd")]
public class EventQueueBenchmarks
{
    private const int EventsPerInvoke = 4_096;
    private EventQueue<int, IEvent> _queue = null!;
    private int _observed;

    [GlobalSetup]
    public void Setup()
    {
        var dispatcher = new Dispatcher<int, IEvent>();
        dispatcher.Listen<BenchmarkEvent>(OnEvent);
        _queue = new EventQueue<int, IEvent>(dispatcher);
    }

    private bool OnEvent(int target, in BenchmarkEvent e)
    {
        _observed += target + e.Value;
        return false;
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvoke)]
    public int EnqueueAndSubmit()
    {
        for (var i = 0; i < EventsPerInvoke; i++) {
            _queue.Send(i & 31, new BenchmarkEvent(i));
        }
        _queue.Submit();
        return _observed;
    }
}
