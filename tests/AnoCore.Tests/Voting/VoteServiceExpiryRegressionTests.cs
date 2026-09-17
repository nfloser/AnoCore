using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.Voting;

[TestClass]
public sealed class VoteServiceExpiryRegressionTests
{
    private static readonly PlayerId Manager = new(76561198000001201);
    private static readonly PlayerId PlayerOne = new(76561198000001202);
    private static readonly PlayerId PlayerTwo = new(76561198000001203);
    private static readonly DateTimeOffset Start = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CastAtDeadline_DoesNotConsumeResultBeforeExpiryOwnerCanFinalize()
    {
        var service = new VoteService(new AllowManagerPermissionEvaluator());
        var definition = new VoteDefinition(
            new VoteId("ano.expiry-regression"),
            "Expiry regression",
            [new VoteOption("a", "Option A"), new VoteOption("b", "Option B")],
            [PlayerOne, PlayerTwo],
            new VotePolicy(TimeSpan.FromSeconds(10), 1, VoteTieBreakPolicy.OptionOrder));
        await service.CreateAsync(Manager, definition, Start);
        Assert.IsTrue((await service.CastAsync(definition.Id, PlayerOne, "b", Start.AddSeconds(1))).Accepted);

        var late = await service.CastAsync(definition.Id, PlayerTwo, "a", Start.AddSeconds(10));
        var expired = service.FinalizeExpired(Start.AddSeconds(10));

        Assert.AreEqual(VoteOperationFailure.NotOpen, late.Failure);
        Assert.HasCount(1, expired);
        Assert.AreEqual(VoteOutcome.Completed, expired[0].Outcome);
        Assert.AreEqual("b", expired[0].WinningOptionId);
    }

    private sealed class AllowManagerPermissionEvaluator : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
    }
}
