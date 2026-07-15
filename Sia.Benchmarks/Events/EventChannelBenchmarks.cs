using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Events", "EventChannel", "EndToEnd")]
public class EventChannelBenchmarks
{
    private const int EventsPerInvoke = 4_096;
    private Dispatcher<int, IEvent> _dispatcher = null!;
    private EventChannel<int, IEvent> _channel = null!;
    private int _observed;

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = new Dispatcher<int, IEvent>();
        _dispatcher.Listen<BenchmarkEvent>(OnEvent);
        _channel = new EventChannel<int, IEvent>();
    }

    private bool OnEvent(int target, in BenchmarkEvent e)
    {
        _observed += target + e.Value;
        return false;
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvoke)]
    public int EnqueueSortAndRelay()
    {
        for (var i = 0; i < EventsPerInvoke; i++) {
            _channel.Send(i & 31, new BenchmarkEvent(i));
        }
        _channel.Relay(_dispatcher);
        return _observed;
    }
}
