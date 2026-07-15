using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Events")]
public class DispatcherBenchmarks
{
    private const int EventsPerInvoke = 4096;
    private Dispatcher<int, IEvent> _dispatcher = null!;
    private int _observed;

    [Params(DispatchRoute.Typed, DispatchRoute.Global, DispatchRoute.Target)]
    public DispatchRoute Route { get; set; }

    [Params(0, 1, 8)]
    public int ListenerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = new Dispatcher<int, IEvent>();
        for (var listenerIndex = 0; listenerIndex < ListenerCount; listenerIndex++) {
            switch (Route) {
                case DispatchRoute.Typed:
                    _dispatcher.Listen<BenchmarkEvent>(OnEvent);
                    break;
                case DispatchRoute.Global:
                    _dispatcher.Listen(new Listener(this));
                    break;
                case DispatchRoute.Target:
                    var listener = new Listener(this);
                    for (var target = 0; target < 32; target++) {
                        _dispatcher.Listen(target, listener);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private bool OnEvent(int target, in BenchmarkEvent e)
    {
        Observe(target);
        return false;
    }

    private void Observe(int target) => _observed += target + 1;

    private sealed class Listener(DispatcherBenchmarks owner) : IEventListener<int>
    {
        public bool OnEvent<TEvent>(int target, in TEvent e)
            where TEvent : IEvent
        {
            owner.Observe(target);
            return false;
        }
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvoke)]
    public int SendEvent()
    {
        for (var i = 0; i < EventsPerInvoke; i++) {
            _dispatcher.Send(i & 31, new BenchmarkEvent(i));
        }
        return _observed;
    }
}
