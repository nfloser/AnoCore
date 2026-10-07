using System.Net;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationWebhookTests
{
    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.1.2.3")]
    [DataRow("169.254.169.254")]
    [DataRow("100.64.0.1")]
    [DataRow("192.168.1.1")]
    [DataRow("::1")]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("2001:db8::1")]
    public void TransportRejectsNonPublicAddresses(string value)
        => Assert.IsFalse(WebhookDestinationPolicy.IsPublic(IPAddress.Parse(value)));

    [TestMethod]
    public void EndpointRequiresApprovedHttpsHostWithoutUserInfoOrFragments()
    {
        var options = new ModerationWebhookConfiguration { Enabled = true, AllowedHosts = ["example.com"] };
        Assert.ThrowsExactly<ArgumentException>(() => WebhookDestinationPolicy.ValidateEndpoint("http://example.com/hook", options));
        Assert.ThrowsExactly<ArgumentException>(() => WebhookDestinationPolicy.ValidateEndpoint("https://user:secret@example.com/hook", options));
        Assert.ThrowsExactly<ArgumentException>(() => WebhookDestinationPolicy.ValidateEndpoint("https://localhost/hook", options));
        Assert.AreEqual("example.com", WebhookDestinationPolicy.ValidateEndpoint("https://example.com/hook?token=secret", options).Host);
        Assert.IsTrue(WebhookDestinationPolicy.IsPublic(IPAddress.Parse("8.8.8.8")));
    }

    [TestMethod]
    public async Task PumpUsesCommittedBoundedFeedDeduplicatesAndDoesNotRetryFailedSends()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var id = Guid.NewGuid().ToString();
        var calls = 0;
        var feed = new[] { new CommittedAdminAudit(id, "moderation.issued", now, "76561198000000101", "private reason") };
        using var pump = new ModerationWebhookPump(new() { Enabled = true, BatchSize = 2, AllowedHosts = ["example.com"] },
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<CommittedAdminAudit>>(feed),
            (_, _) => { calls++; throw new HttpRequestException("secret URI must never be reported"); });
        await pump.CheckpointAsync(now);
        await pump.CheckpointAsync(now);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1L, pump.Failures);
        var payload = ModerationWebhookPump.Payload(feed[0], new());
        Assert.IsFalse(payload.Contains("private reason", StringComparison.Ordinal));
        Assert.IsFalse(payload.Contains("76561198000000101", StringComparison.Ordinal));
        Assert.IsTrue(payload.Contains("allowed_mentions", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DisposalCancelsAnInFlightDelivery()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        using var pump = new ModerationWebhookPump(new() { Enabled = true, AllowedHosts = ["example.com"] },
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<CommittedAdminAudit>>(
                [new("cancel", "admin.kick", now, null, "")]),
            async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var checkpoint = pump.CheckpointAsync(now).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        pump.Dispose();
        await checkpoint.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0L, pump.Failures);
    }

    [TestMethod]
    public async Task DisabledPumpDoesNotReadOrSendAndDisposedPumpCannotDeliver()
    {
        var read = 0;
        using var pump = new ModerationWebhookPump(new(), (_, _, _) =>
        { read++; return ValueTask.FromResult<IReadOnlyList<CommittedAdminAudit>>([]); }, (_, _) => ValueTask.CompletedTask);
        await pump.CheckpointAsync(DateTimeOffset.UtcNow);
        Assert.AreEqual(0, read);
        pump.Dispose();
        await pump.CheckpointAsync(DateTimeOffset.UtcNow);
        Assert.AreEqual(0, read);
    }
}
