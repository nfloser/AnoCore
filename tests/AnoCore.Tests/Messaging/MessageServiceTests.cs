using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Messaging;

[TestClass]
public sealed class MessageServiceTests
{
    [TestMethod]
    public async Task SendAsync_WithoutTransportIsUnavailable()
    {
        using var service = new MessageService();

        var result = await service.SendAsync(Chat("hello"));

        Assert.AreEqual(MessageDispatchStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public async Task SendAsync_HigherPriorityCenterMessageSuppressesLowerPriority()
    {
        var delays = new ManualDelay();
        using var service = new MessageService(delays.DelayAsync);
        var transport = new RecordingTransport();
        using var registration = service.AttachTransport(transport);

        var target = MessageTarget.ForAll();
        var first = await service.SendAsync(
            new MessageRequest(target, MessageChannel.Center, "important", 50, TimeSpan.FromSeconds(5)));
        var second = await service.SendAsync(
            new MessageRequest(target, MessageChannel.Center, "noise", 10, TimeSpan.FromSeconds(5)));

        Assert.IsTrue(first.Delivered);
        Assert.AreEqual(MessageDispatchStatus.Suppressed, second.Status);
        Assert.AreEqual(1, transport.Sent.Count);
        Assert.AreEqual("important", transport.Sent[0].Text);
    }

    [TestMethod]
    public async Task SendAsync_EqualPriorityReplacesLease_AndOldExpiryCannotClearReplacement()
    {
        var delays = new ManualDelay();
        using var service = new MessageService(delays.DelayAsync);
        var transport = new RecordingTransport();
        using var registration = service.AttachTransport(transport);
        var target = MessageTarget.ForAll();

        await service.SendAsync(
            new MessageRequest(target, MessageChannel.CenterHtml, "first", 20, TimeSpan.FromSeconds(5)));
        await service.SendAsync(
            new MessageRequest(target, MessageChannel.CenterHtml, "second", 20, TimeSpan.FromSeconds(5)));

        delays.Complete(0);
        await Task.Yield();

        Assert.AreEqual(0, transport.Cleared.Count);

        delays.Complete(1);
        await WaitUntilAsync(() => transport.Cleared.Count == 1);

        Assert.AreEqual(MessageChannel.CenterHtml, transport.Cleared[0].Channel);
    }

    [TestMethod]
    public async Task SendAsync_TransportFailureReleasesPrioritySlot()
    {
        var delays = new ManualDelay();
        using var service = new MessageService(delays.DelayAsync);
        var transport = new RecordingTransport { NextSendResult = false };
        using var registration = service.AttachTransport(transport);
        var target = MessageTarget.ForAll();

        var failed = await service.SendAsync(
            new MessageRequest(target, MessageChannel.Center, "first", 100, TimeSpan.FromSeconds(5)));

        transport.NextSendResult = true;
        var retry = await service.SendAsync(
            new MessageRequest(target, MessageChannel.Center, "retry", 1, TimeSpan.FromSeconds(5)));

        Assert.AreEqual(MessageDispatchStatus.Unavailable, failed.Status);
        Assert.IsTrue(retry.Delivered);
    }

    [TestMethod]
    public async Task Dispose_CancelsInFlightDelivery()
    {
        var transport = new BlockingTransport();
        var service = new MessageService();
        using var registration = service.AttachTransport(transport);

        var send = service.SendAsync(Chat("pending")).AsTask();
        await transport.Started.Task;

        service.Dispose();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await send);
    }

    [TestMethod]
    public async Task ExpiryClearFailure_IsolatedFromBackgroundTask()
    {
        var delays = new ManualDelay();
        var failures = new List<Exception>();
        using var service = new MessageService(delays.DelayAsync, failures.Add);
        var transport = new RecordingTransport { ThrowOnClear = true };
        using var registration = service.AttachTransport(transport);

        await service.SendAsync(new MessageRequest(
            MessageTarget.ForAll(),
            MessageChannel.Center,
            "temporary",
            duration: TimeSpan.FromSeconds(1)));

        delays.Complete(0);
        await WaitUntilAsync(() => failures.Count == 1);

        Assert.IsInstanceOfType<InvalidOperationException>(failures[0]);
    }

    [TestMethod]
    public void MessageRequest_RejectsInvalidChatDurationAndPriority()
    {
        var target = MessageTarget.ForAll();

        Assert.ThrowsException<ArgumentException>(() =>
            new MessageRequest(target, MessageChannel.Chat, "chat", duration: TimeSpan.FromSeconds(1)));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            new MessageRequest(target, MessageChannel.Center, "center", priority: MessageRequest.MaxPriority + 1));
    }

    [TestMethod]
    public void MessageTarget_RejectsUnknownTeam()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            MessageTarget.ForTeam(PlayerTeam.Unknown));
    }

    private static MessageRequest Chat(string text)
        => new(MessageTarget.ForPlayer(new PlayerId(76561198000000001)), MessageChannel.Chat, text);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        public List<MessageRequest> Sent { get; } = [];
        public List<(MessageTarget Target, MessageChannel Channel)> Cleared { get; } = [];
        public bool NextSendResult { get; set; } = true;
        public bool ThrowOnClear { get; set; }

        public ValueTask<bool> TrySendAsync(
            MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(request);
            return ValueTask.FromResult(NextSendResult);
        }

        public ValueTask<bool> TryClearAsync(
            MessageTarget target,
            MessageChannel channel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnClear)
            {
                throw new InvalidOperationException("clear failed");
            }

            Cleared.Add((target, channel));
            return ValueTask.FromResult(true);
        }
    }

    private sealed class BlockingTransport : IMessageTransport
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<bool> TrySendAsync(
            MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }

        public ValueTask<bool> TryClearAsync(
            MessageTarget target,
            MessageChannel channel,
            CancellationToken cancellationToken = default)
        {
            _ = target;
            _ = channel;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _pending = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pending.Add(completion);
            return completion.Task;
        }

        public void Complete(int index)
            => _pending[index].TrySetResult();
    }
}
