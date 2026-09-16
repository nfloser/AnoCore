using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Runtime.Voting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Voting;

[TestClass]
public sealed class VoteServiceTests
{
    private static readonly PlayerId Manager = new(76561198000001001);
    private static readonly PlayerId PlayerOne = new(76561198000001002);
    private static readonly PlayerId PlayerTwo = new(76561198000001003);
    private static readonly PlayerId Outsider = new(76561198000001004);
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateAsync_RequiresManagementPermission()
    {
        var service = new VoteService(new DenyAllPermissions());

        var result = await service.CreateAsync(Manager, CreateDefinition(), Start);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(VoteOperationFailure.Forbidden, result.Failure);
        Assert.IsFalse(service.TryGet(new VoteId("ano.test"), out _));
    }

    [TestMethod]
    public async Task CastAsync_AllowsExactlyOneVotePerEligibleSteamId()
    {
        var service = new VoteService(new AllowAllPermissions());
        await service.CreateAsync(Manager, CreateDefinition(), Start);

        var first = await service.CastAsync(new VoteId("ano.test"), PlayerOne, "b", Start.AddSeconds(1));
        var second = await service.CastAsync(new VoteId("ano.test"), PlayerOne, "a", Start.AddSeconds(2));
        var outsider = await service.CastAsync(new VoteId("ano.test"), Outsider, "a", Start.AddSeconds(2));

        Assert.IsTrue(first.Accepted);
        Assert.AreEqual(VoteOperationFailure.AlreadyVoted, second.Failure);
        Assert.AreEqual(VoteOperationFailure.NotEligible, outsider.Failure);
    }

    [TestMethod]
    public async Task CastAsync_ReconnectWithSameSteamIdCannotVoteTwice()
    {
        var service = new VoteService(new AllowAllPermissions());
        await service.CreateAsync(Manager, CreateDefinition(), Start);
        await service.CastAsync(new VoteId("ano.test"), PlayerOne, "a", Start.AddSeconds(1));

        var reconnectIdentity = new PlayerId(PlayerOne.SteamId64);
        var result = await service.CastAsync(new VoteId("ano.test"), reconnectIdentity, "b", Start.AddSeconds(2));

        Assert.AreEqual(VoteOperationFailure.AlreadyVoted, result.Failure);
    }

    [TestMethod]
    public async Task CloseAsync_UsesDeterministicOptionOrderForTies()
    {
        var service = new VoteService(new AllowAllPermissions());
        await service.CreateAsync(Manager, CreateDefinition(), Start);
        await service.CastAsync(new VoteId("ano.test"), PlayerOne, "b", Start.AddSeconds(1));
        await service.CastAsync(new VoteId("ano.test"), PlayerTwo, "a", Start.AddSeconds(2));

        var closed = await service.CloseAsync(Manager, new VoteId("ano.test"), Start.AddSeconds(3));

        Assert.IsTrue(closed.Accepted);
        Assert.IsNotNull(closed.Result);
        Assert.AreEqual(VoteOutcome.Completed, closed.Result.Outcome);
        Assert.AreEqual("a", closed.Result.WinningOptionId);
        Assert.AreEqual(1, closed.Result.Tallies["a"]);
        Assert.AreEqual(1, closed.Result.Tallies["b"]);
    }

    [TestMethod]
    public async Task CloseAsync_FailsQuorumWithoutInventingWinner()
    {
        var service = new VoteService(new AllowAllPermissions());
        var definition = CreateDefinition(minimumVotes: 2);
        await service.CreateAsync(Manager, definition, Start);
        await service.CastAsync(definition.Id, PlayerOne, "a", Start.AddSeconds(1));

        var closed = await service.CloseAsync(Manager, definition.Id, Start.AddSeconds(2));

        Assert.AreEqual(VoteOutcome.QuorumNotMet, closed.Result!.Outcome);
        Assert.IsNull(closed.Result.WinningOptionId);
    }

    [TestMethod]
    public async Task FinalizeExpired_ClosesAtDeadlineAndRejectsLateVotes()
    {
        var service = new VoteService(new AllowAllPermissions());
        var definition = CreateDefinition(duration: TimeSpan.FromSeconds(10), minimumVotes: 1);
        await service.CreateAsync(Manager, definition, Start);
        await service.CastAsync(definition.Id, PlayerOne, "b", Start.AddSeconds(1));

        var results = service.FinalizeExpired(Start.AddSeconds(10));
        var late = await service.CastAsync(definition.Id, PlayerTwo, "a", Start.AddSeconds(11));

        Assert.HasCount(1, results);
        Assert.AreEqual("b", results[0].WinningOptionId);
        Assert.AreEqual(VoteOperationFailure.NotOpen, late.Failure);
    }

    [TestMethod]
    public async Task CancelAsync_RequiresPermissionAndProducesCancelledResult()
    {
        var permissions = new TogglePermissions { Allowed = true };
        var service = new VoteService(permissions);
        var definition = CreateDefinition();
        await service.CreateAsync(Manager, definition, Start);
        permissions.Allowed = false;

        var denied = await service.CancelAsync(Manager, definition.Id, Start.AddSeconds(1));
        permissions.Allowed = true;
        var cancelled = await service.CancelAsync(Manager, definition.Id, Start.AddSeconds(2));

        Assert.AreEqual(VoteOperationFailure.Forbidden, denied.Failure);
        Assert.AreEqual(VoteOutcome.Cancelled, cancelled.Result!.Outcome);
    }

    [TestMethod]
    public async Task CreateAsync_RejectsDuplicateActiveVoteId()
    {
        var service = new VoteService(new AllowAllPermissions());
        var definition = CreateDefinition();
        await service.CreateAsync(Manager, definition, Start);

        var duplicate = await service.CreateAsync(Manager, definition, Start.AddSeconds(1));

        Assert.AreEqual(VoteOperationFailure.AlreadyExists, duplicate.Failure);
    }

    private static VoteDefinition CreateDefinition(TimeSpan? duration = null, int minimumVotes = 1)
        => new(
            new VoteId("ano.test"),
            "Test vote",
            [new VoteOption("a", "Option A"), new VoteOption("b", "Option B")],
            [PlayerOne, PlayerTwo],
            new VotePolicy(duration ?? TimeSpan.FromMinutes(1), minimumVotes, VoteTieBreakPolicy.OptionOrder));

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class TogglePermissions : IPermissionEvaluator
    {
        public bool Allowed { get; set; }
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Allowed);
    }
}
