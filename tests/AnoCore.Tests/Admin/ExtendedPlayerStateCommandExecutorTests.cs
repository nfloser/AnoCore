using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedPlayerStateCommandExecutorTests
{
    private static readonly PlayerId ActorId = new(76561198000016501);
    private static readonly PlayerId TargetId = new(76561198000016502);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 14, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task HealthBoundsAreRejectedBeforeTargetResolution()
    {
        var targets = new FakeTargets(ModerationTargetResult.Success(Player()));
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);
        var executor = new ExtendedPlayerStateCommandExecutor(targets, service);

        var low = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.SetHealth, ActorId, "Target", 0);
        var high = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.SetHealth, ActorId, "Target", 1001);

        Assert.IsFalse(low.Success);
        Assert.IsFalse(high.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, low.FailureReason);
        Assert.AreEqual(0, targets.Calls);
        Assert.AreEqual(0, transport.Applied.Count);
    }

    [TestMethod]
    public async Task SpeedAndBlindUseValidatedValues()
    {
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);
        var executor = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())), service);

        var speed = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.SetSpeed, ActorId, "Target", 175);
        var blind = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.Blind, ActorId, "Target", null);

        Assert.IsTrue(speed.Success);
        Assert.IsTrue(blind.Success);
        Assert.AreEqual(2, transport.Applied.Count);
        Assert.AreEqual(175, transport.Applied[0].Mutation.Value);
        Assert.AreEqual(255, transport.Applied[1].Mutation.Value);
        Assert.AreEqual(ExtendedPlayerStateFacet.Speed, transport.Captured[0].Facet);
        Assert.AreEqual(ExtendedPlayerStateFacet.Blindness, transport.Captured[1].Facet);
    }

    [TestMethod]
    public async Task ImmuneAndOfflineTargetsNeverReachNativeTransport()
    {
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);

        var immune = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Reject(
                ModerationTargetFailure.TargetImmune)), service);
        var denied = await immune.ExecuteAsync(
            ExtendedPlayerStateOperation.Freeze, ActorId, "Target", null);

        var offline = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(TargetId)), service);
        var missing = await offline.ExecuteAsync(
            ExtendedPlayerStateOperation.Freeze, ActorId, TargetId.ToString(), null);

        Assert.IsFalse(denied.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, denied.FailureReason);
        Assert.IsFalse(missing.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.AreEqual(0, transport.Applied.Count);
    }

    [TestMethod]
    public async Task ReversibleMovementCapturesOriginalStateOnceAndRestoresIt()
    {
        var player = Player();
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);
        var executor = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(player)), service);

        Assert.IsTrue((await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.Freeze, ActorId, "Target", null)).Success);
        Assert.IsTrue((await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.Noclip, ActorId, "Target", null)).Success);
        Assert.IsTrue((await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.Walk, ActorId, "Target", null)).Success);

        Assert.AreEqual(1,
            transport.Captured.Count(x => x.Facet == ExtendedPlayerStateFacet.Movement));
        Assert.AreEqual(2, transport.Applied.Count);
        Assert.AreEqual(1, transport.Restored.Count);
        Assert.AreEqual(ExtendedPlayerStateFacet.Movement, transport.Restored[0].Baseline.Facet);
        Assert.AreEqual(player.SessionId, transport.Restored[0].Player.SessionId);
    }

    [TestMethod]
    public async Task ResetWithoutOwnedStateIsSafeAndDoesNotTouchNativeState()
    {
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);
        var executor = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())), service);

        var result = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.ResetSpeed, ActorId, "Target", null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, transport.Captured.Count);
        Assert.AreEqual(0, transport.Applied.Count);
        Assert.AreEqual(0, transport.Restored.Count);
    }

    [TestMethod]
    public async Task SessionCleanupRestoresEveryOwnedFacetButNotAnotherSession()
    {
        var first = Player();
        var second = first with { SessionId = PlayerSessionId.New() };
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);

        await service.ApplyAsync(
            first, new ExtendedPlayerStateMutation(
                ExtendedPlayerStateOperation.Freeze));
        await service.ApplyAsync(
            first, new ExtendedPlayerStateMutation(
                ExtendedPlayerStateOperation.SetSpeed, 150));
        await service.ApplyAsync(
            first, new ExtendedPlayerStateMutation(
                ExtendedPlayerStateOperation.God));

        await service.ReleaseSessionAsync(first);
        await service.ReleaseSessionAsync(second);

        Assert.AreEqual(3, transport.Restored.Count);
        Assert.IsTrue(transport.Restored.All(x => x.Player.SessionId == first.SessionId));
    }

    [TestMethod]
    public async Task OneShotActionsDoNotCaptureReversibleBaseline()
    {
        var player = Player();
        var transport = new RecordingTransport();
        var service = new ExtendedPlayerStateService(transport);
        var executor = new ExtendedPlayerStateCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(player)), service);

        var health = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.SetHealth, null, "Target", 250);
        var armor = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.SetArmor, null, "Target", 100);
        var slay = await executor.ExecuteAsync(
            ExtendedPlayerStateOperation.Slay, null, "Target", null);

        Assert.IsTrue(health.Success);
        Assert.IsTrue(armor.Success);
        Assert.IsTrue(slay.Success);
        Assert.AreEqual(0, transport.Captured.Count);
        Assert.AreEqual(3, transport.Applied.Count);
    }

    private static PlayerSnapshot Player() => new(
        TargetId,
        PlayerSessionId.New(),
        "Target",
        true,
        true,
        PlayerTeam.CounterTerrorist,
        Now,
        Now);

    private sealed class FakeTargets(ModerationTargetResult result)
        : IModerationTargetGateway
    {
        public int Calls { get; private set; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector,
            PlayerId? actor,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingTransport : IExtendedPlayerStateTransport
    {
        public List<(PlayerSnapshot Player, ExtendedPlayerStateFacet Facet)> Captured { get; } = [];
        public List<(PlayerSnapshot Player, ExtendedPlayerStateMutation Mutation)> Applied { get; } = [];
        public List<(PlayerSnapshot Player, ExtendedPlayerStateBaseline Baseline)> Restored { get; } = [];

        public ValueTask<ExtendedPlayerStateBaseline> CaptureAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateFacet facet,
            CancellationToken cancellationToken = default)
        {
            Captured.Add((player, facet));
            return ValueTask.FromResult(new ExtendedPlayerStateBaseline(facet));
        }

        public ValueTask ApplyAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateMutation mutation,
            CancellationToken cancellationToken = default)
        {
            Applied.Add((player, mutation));
            return ValueTask.CompletedTask;
        }

        public ValueTask RestoreAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateBaseline baseline,
            CancellationToken cancellationToken = default)
        {
            Restored.Add((player, baseline));
            return ValueTask.CompletedTask;
        }
    }
}
