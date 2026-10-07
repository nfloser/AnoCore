using AnoCore.Abstractions.Events;
using AnoCore.Runtime.Events;

namespace AnoCore.Tests.Events;

[TestClass]
public sealed class CoreUnloadNotifierTests
{
    [TestMethod]
    public async Task UnloadIsOneShotAndKeepsCapturedReasonAndUtcTime()
    {
        var bus = new AnoEventBus();
        var received = new List<CoreUnloadingEvent>();
        var at = new DateTimeOffset(2026, 10, 7, 16, 0, 0, TimeSpan.FromHours(2));
        using var subscription = bus.Subscribe<CoreUnloadingEvent>((item, _) =>
        {
            received.Add(item);
            return ValueTask.CompletedTask;
        });
        var notifier = new CoreUnloadNotifier(bus, () => at);
        await notifier.NotifyAsync(true);
        await notifier.NotifyAsync(false);
        Assert.HasCount(1, received);
        Assert.IsTrue(received[0].HotReload);
        Assert.AreEqual(at.ToUniversalTime(), received[0].OccurredAtUtc);
        Assert.AreEqual(TimeSpan.Zero, received[0].OccurredAtUtc.Offset);
    }

    [TestMethod]
    public async Task FailingObserversAndDiagnosticsCannotPreventTeardownOrRepeatNotification()
    {
        var bus = new AnoEventBus();
        var calls = 0;
        using var failing = bus.Subscribe<CoreUnloadingEvent>((_, _) => throw new InvalidOperationException("observer"));
        using var healthy = bus.Subscribe<CoreUnloadingEvent>((_, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });
        var notifier = new CoreUnloadNotifier(bus, reportError: _ => throw new InvalidOperationException("diagnostics"));
        await notifier.NotifyAsync(false);
        await notifier.NotifyAsync(false);
        Assert.AreEqual(1, calls);
    }
}
