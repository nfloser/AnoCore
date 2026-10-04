using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedPositionCommandExecutorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 15, 30, 0, TimeSpan.Zero);
    private static readonly PlayerId ActorId = new(76561198000016601);
    private static readonly PlayerId TargetId = new(76561198000016602);
    private static readonly PlayerId DestinationId = new(76561198000016603);

    [TestMethod]
    public async Task InvalidCoordinatesFailBeforeTargetResolution()
    {
        var targets = new FakeTargets(ModerationTargetResult.Success(Player(TargetId, true)));
        var service = new ExtendedPositionService(new RecordingTransport());
        var executor = new ExtendedPositionCommandExecutor(
            targets,
            new FakeResolver(Player(DestinationId, true)),
            service);

        var result = await executor.ExecuteAsync(
            ExtendedPositionOperation.TeleportPosition,
            ActorId,
            "Target",
            ["nan", "10", "20"]);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.AreEqual(0, targets.Calls);
    }

    [TestMethod]
    public async Task ReviveRequiresCurrentSessionDeathPositionThenRespawnsAndTeleports()
    {
        var dead = Player(TargetId, false);
        var transport = new RecordingTransport();
        var service = new ExtendedPositionService(transport);
        var executor = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(dead)),
            new FakeResolver(Player(DestinationId, true)),
            service);

        var missing = await executor.ExecuteAsync(
            ExtendedPositionOperation.Revive,
            ActorId,
            "Target",
            []);

        service.RecordDeathPosition(dead, new PlayerWorldPosition(100, 200, 300));
        var revived = await executor.ExecuteAsync(
            ExtendedPositionOperation.Revive,
            ActorId,
            "Target",
            []);

        Assert.IsFalse(missing.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.IsTrue(revived.Success);
        Assert.AreEqual(1, transport.Respawned.Count);
        Assert.AreEqual(
            new PlayerWorldPosition(100, 200, 300),
            transport.Teleports.Single().Position);
    }

    [TestMethod]
    public async Task RespawnRejectsLivingTargetAndDoesNotNeedDeathPosition()
    {
        var transport = new RecordingTransport();
        var living = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player(TargetId, true))),
            new FakeResolver(Player(DestinationId, true)),
            new ExtendedPositionService(transport));

        var rejected = await living.ExecuteAsync(
            ExtendedPositionOperation.Respawn,
            ActorId,
            "Target",
            []);

        var deadPlayer = Player(TargetId, false);
        var dead = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(deadPlayer)),
            new FakeResolver(Player(DestinationId, true)),
            new ExtendedPositionService(transport));
        var accepted = await dead.ExecuteAsync(
            ExtendedPositionOperation.Respawn,
            ActorId,
            "Target",
            []);

        Assert.IsFalse(rejected.Success);
        Assert.IsTrue(accepted.Success);
        Assert.AreEqual(1, transport.Respawned.Count);
    }

    [TestMethod]
    public async Task TeleportToPlayerReadsDestinationAndMovesOnlyAuthorizedTarget()
    {
        var target = Player(TargetId, true);
        var destination = Player(DestinationId, true);
        var transport = new RecordingTransport();
        transport.Positions[destination.SessionId] = new PlayerWorldPosition(11, 22, 33);
        var executor = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(target)),
            new FakeResolver(destination),
            new ExtendedPositionService(transport));

        var result = await executor.ExecuteAsync(
            ExtendedPositionOperation.TeleportPlayer,
            ActorId,
            "Target",
            ["Destination"]);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(target.SessionId, transport.Teleports.Single().Player.SessionId);
        Assert.AreEqual(new PlayerWorldPosition(11, 22, 33), transport.Teleports.Single().Position);
    }

    [TestMethod]
    public async Task BuryAndUnburyUseReferenceOffsets()
    {
        var target = Player(TargetId, true);
        var transport = new RecordingTransport();
        transport.Positions[target.SessionId] = new PlayerWorldPosition(50, 60, 100);
        var service = new ExtendedPositionService(transport);
        var executor = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(target)),
            new FakeResolver(Player(DestinationId, true)),
            service);

        var buried = await executor.ExecuteAsync(
            ExtendedPositionOperation.Bury,
            ActorId,
            "Target",
            []);

        transport.Positions[target.SessionId] = new PlayerWorldPosition(50, 60, 75);
        var unburied = await executor.ExecuteAsync(
            ExtendedPositionOperation.Unbury,
            ActorId,
            "Target",
            []);

        Assert.IsTrue(buried.Success);
        Assert.IsTrue(unburied.Success);
        Assert.AreEqual(75f, transport.Teleports[0].Position.Z);
        Assert.AreEqual(105f, transport.Teleports[1].Position.Z);
    }

    [TestMethod]
    public async Task SlapDefaultsToZeroDamageAndRejectsUnsafeDamageBeforeTargeting()
    {
        var target = Player(TargetId, true);
        var targets = new FakeTargets(ModerationTargetResult.Success(target));
        var transport = new RecordingTransport();
        var executor = new ExtendedPositionCommandExecutor(
            targets,
            new FakeResolver(Player(DestinationId, true)),
            new ExtendedPositionService(transport));

        var defaulted = await executor.ExecuteAsync(
            ExtendedPositionOperation.Slap,
            ActorId,
            "Target",
            []);
        var invalid = await executor.ExecuteAsync(
            ExtendedPositionOperation.Slap,
            ActorId,
            "Target",
            ["1001"]);

        Assert.IsTrue(defaulted.Success);
        Assert.AreEqual(0, transport.Slaps.Single().Damage);
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(1, targets.Calls);
    }

    [TestMethod]
    public async Task OldDeathLocationCannotLeakAcrossReconnect()
    {
        var oldSession = Player(TargetId, false);
        var newSession = new PlayerSnapshot(
            oldSession.Id,
            PlayerSessionId.New(),
            oldSession.Name,
            true,
            false,
            oldSession.Team,
            Now.AddMinutes(1),
            Now.AddMinutes(1));
        var transport = new RecordingTransport();
        var service = new ExtendedPositionService(transport);
        service.RecordDeathPosition(oldSession, new PlayerWorldPosition(1, 2, 3));
        await service.ForgetSessionAsync(oldSession.SessionId);

        var executor = new ExtendedPositionCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(newSession)),
            new FakeResolver(Player(DestinationId, true)),
            service);

        var result = await executor.ExecuteAsync(
            ExtendedPositionOperation.Revive,
            ActorId,
            "Target",
            []);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, transport.Respawned.Count);
        Assert.AreEqual(0, transport.Teleports.Count);
    }

    private static PlayerSnapshot Player(PlayerId id, bool alive) => new(
        id,
        PlayerSessionId.New(),
        id == DestinationId ? "Destination" : "Target",
        true,
        alive,
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

    private sealed class FakeResolver(PlayerSnapshot player) : IPlayerTargetResolver
    {
        public TargetResolutionResult Resolve(
            string selector,
            PlayerId? caller = null,
            TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.None)
            => string.Equals(selector, player.Name, StringComparison.OrdinalIgnoreCase)
                ? TargetResolutionResult.Success([player])
                : TargetResolutionResult.Reject(TargetResolutionFailure.NotFound);
    }

    private sealed class RecordingTransport : IExtendedPositionTransport
    {
        public Dictionary<PlayerSessionId, PlayerWorldPosition> Positions { get; } = [];
        public List<PlayerSnapshot> Respawned { get; } = [];
        public List<(PlayerSnapshot Player, PlayerWorldPosition Position)> Teleports { get; } = [];
        public List<(PlayerSnapshot Player, int Damage)> Slaps { get; } = [];

        public ValueTask<PlayerWorldPosition> ReadPositionAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            if (!Positions.TryGetValue(player.SessionId, out var position))
            {
                position = new PlayerWorldPosition(0, 0, 0);
            }

            return ValueTask.FromResult(position);
        }

        public ValueTask RespawnAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            Respawned.Add(player);
            return ValueTask.CompletedTask;
        }

        public ValueTask TeleportAsync(
            PlayerSnapshot player,
            PlayerWorldPosition position,
            CancellationToken cancellationToken = default)
        {
            Teleports.Add((player, position));
            Positions[player.SessionId] = position;
            return ValueTask.CompletedTask;
        }

        public ValueTask SlapAsync(
            PlayerSnapshot player,
            int damage,
            CancellationToken cancellationToken = default)
        {
            Slaps.Add((player, damage));
            return ValueTask.CompletedTask;
        }
    }
}
