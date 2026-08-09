#if !BROWSER
using System.Threading.Channels;
using Sia_Examples.Console.Layout;

namespace Sia_Examples.Console;

internal sealed partial class ConsoleDomBackend
{
    private readonly Channel<string> _events = Channel.CreateUnbounded<string>(
        new() {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Lock _scheduleLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _scheduled = [];

    public async Task<string> WaitForEventAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        while (true) {
            if (_pendingEditorEvents.Count > 0) {
                return _pendingEditorEvents.Dequeue()();
            }

            using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var keyTask = _terminal.ReadKeyAsync(pending.Token).AsTask();
            var eventTask = _events.Reader.ReadAsync(pending.Token).AsTask();
            var completed = await Task.WhenAny(keyTask, eventTask);
            if (ReferenceEquals(completed, eventTask)) {
                var payload = await eventTask;
                pending.Cancel();
                await ObserveCancellationAsync(keyTask);
                return payload;
            }

            var key = await keyTask;
            pending.Cancel();
            await ObserveCancellationAsync(eventTask);

            if (_editMode != EditMode.None && HandleEditorKey(key)) {
                Flush();
                if (_pendingEditorEvents.Count > 0) {
                    return _pendingEditorEvents.Dequeue()();
                }
                continue;
            }

            switch (key.Key) {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return "quit";
                case ConsoleKey.UpArrow:
                    MoveFocus(-1);
                    Flush();
                    break;
                case ConsoleKey.DownArrow:
                    MoveFocus(1);
                    Flush();
                    break;
                case ConsoleKey.LeftArrow:
                    SwitchPane(Pane.Sidebar);
                    Flush();
                    break;
                case ConsoleKey.RightArrow:
                    SwitchPane(Pane.Content);
                    Flush();
                    break;
                case ConsoleKey.Tab:
                    TogglePane();
                    Flush();
                    break;
                case ConsoleKey.Home:
                    MoveFocusToBoundary(first: true);
                    Flush();
                    break;
                case ConsoleKey.End:
                    MoveFocusToBoundary(first: false);
                    Flush();
                    break;
                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    if (_focused is { } target && TryActivateEditor(target)) {
                        Flush();
                        break;
                    }
                    if (ActivateFocused() is { } payload) {
                        return payload;
                    }
                    break;
            }
        }
    }

    public void ScheduleEvent(string key, string payload, int delayMilliseconds)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_scheduleLock) {
            _scheduled.Remove(key, out previous);
            _scheduled.Add(key, cancellation);
        }
        previous?.Cancel();
        _ = PublishScheduledAsync(
            key,
            payload,
            Math.Max(delayMilliseconds, 0),
            cancellation);
    }

    public void CancelScheduledEvent(string key)
    {
        CancellationTokenSource? cancellation;
        lock (_scheduleLock) {
            _scheduled.Remove(key, out cancellation);
        }
        cancellation?.Cancel();
    }

    private async Task PublishScheduledAsync(
        string key,
        string payload,
        int delayMilliseconds,
        CancellationTokenSource cancellation)
    {
        try {
            await Task.Delay(delayMilliseconds, cancellation.Token);
            _events.Writer.TryWrite(payload);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        } finally {
            lock (_scheduleLock) {
                if (_scheduled.TryGetValue(key, out var current)
                    && ReferenceEquals(current, cancellation)) {
                    _scheduled.Remove(key);
                }
            }
            cancellation.Dispose();
        }
    }

    private static async Task ObserveCancellationAsync(Task operation)
    {
        try {
            await operation;
        }
        catch (OperationCanceledException) {
        }
    }

    private void DisposeEvents()
    {
        CancellationTokenSource[] scheduled;
        lock (_scheduleLock) {
            scheduled = [.. _scheduled.Values];
            _scheduled.Clear();
        }
        foreach (var cancellation in scheduled) {
            cancellation.Cancel();
        }
        _events.Writer.TryComplete();
    }
}
#endif
