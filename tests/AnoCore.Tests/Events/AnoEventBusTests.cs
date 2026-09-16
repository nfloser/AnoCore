using AnoCore.Abstractions.Events;
using AnoCore.Runtime.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Events;

[TestClass]
public sealed class AnoEventBusTests
{
    [TestMethod]
    public async Task PublishAsync_DeliversSubscribersInRegistrationOrder()
    {
        var bus = new AnoEventBus();
        var calls = new List<int>();

        using var first = bus.Subscribe<TestEvent>((_, _) =>
        {
            calls.Add(1);
            return ValueTask.CompletedTask;
        });
        using var second = bus.Subscribe<TestEvent>((_, _) =>
        {
            calls.Add(2);
            return ValueTask.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("hello"));

        CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
    }

    [TestMethod]
    public async Task Dispose_PreventsFutureDeliveryAndIsIdempotent()
    {
        var bus = new AnoEventBus();
        var calls = 0;
        var subscription = bus.Subscribe<TestEvent>((_, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("first"));
        subscription.Dispose();
        subscription.Dispose();
        await bus.PublishAsync(new TestEvent("second"));

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task DuplicateHandlerSubscriptions_AreIndependent()
    {
        var bus = new AnoEventBus();
        var calls = 0;
        ValueTask Handler(TestEvent _, CancellationToken __)
        {
            calls++;
            return ValueTask.CompletedTask;
        }

        var first = bus.Subscribe<TestEvent>(Handler);
        using var second = bus.Subscribe<TestEvent>(Handler);
        first.Dispose();

        await bus.PublishAsync(new TestEvent("value"));

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task PublishAsync_IsolatesEventTypes()
    {
        var bus = new AnoEventBus();
        var testCalls = 0;
        var otherCalls = 0;

        using var first = bus.Subscribe<TestEvent>((_, _) =>
        {
            testCalls++;
            return ValueTask.CompletedTask;
        });
        using var second = bus.Subscribe<OtherEvent>((_, _) =>
        {
            otherCalls++;
            return ValueTask.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("value"));

        Assert.AreEqual(1, testCalls);
        Assert.AreEqual(0, otherCalls);
    }

    [TestMethod]
    public async Task PublishAsync_ContinuesAfterSubscriberFailureAndAggregatesFailures()
    {
        var bus = new AnoEventBus();
        var laterSubscriberCalled = false;

        using var failing = bus.Subscribe<TestEvent>((_, _) =>
            ValueTask.FromException(new InvalidOperationException("boom")));
        using var later = bus.Subscribe<TestEvent>((_, _) =>
        {
            laterSubscriberCalled = true;
            return ValueTask.CompletedTask;
        });

        var exception = await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await bus.PublishAsync(new TestEvent("value")));

        Assert.IsTrue(laterSubscriberCalled);
        Assert.HasCount(1, exception.InnerExceptions);
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerExceptions[0]);
    }

    [TestMethod]
    public async Task PublishAsync_PreCancelledTokenInvokesNoSubscribers()
    {
        var bus = new AnoEventBus();
        var called = false;
        using var subscription = bus.Subscribe<TestEvent>((_, _) =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await bus.PublishAsync(new TestEvent("value"), cancellation.Token));

        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task DisposeDuringPublish_AffectsOnlyFuturePublications()
    {
        var bus = new AnoEventBus();
        var calls = new List<string>();
        IDisposable? second = null;

        using var first = bus.Subscribe<TestEvent>((_, _) =>
        {
            calls.Add("first");
            second!.Dispose();
            return ValueTask.CompletedTask;
        });
        second = bus.Subscribe<TestEvent>((_, _) =>
        {
            calls.Add("second");
            return ValueTask.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("one"));
        await bus.PublishAsync(new TestEvent("two"));

        CollectionAssert.AreEqual(new[] { "first", "second", "first" }, calls);
    }

    private sealed record TestEvent(string Value) : IAnoEvent;

    private sealed record OtherEvent : IAnoEvent;
}
