using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class SessionBoundVetoVoteServiceTests
{
    private static readonly PlayerId Host = new(76561198000000501);
    private static readonly PlayerId Normal = new(76561198000000502);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T10:00:00Z");

    [TestMethod]
    public async Task Management_UsesDedicatedFlagWhileParticipationAndSharedVotesStayIndependent()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.IsTrue((await fixture.Run(fixture.Votes.CreateAsync(Host, Definition(), Now))).Accepted);
        Assert.AreEqual(VoteOperationFailure.Forbidden,
            (await fixture.Run(fixture.Votes.CancelAsync(Normal, AnoVetoCoordinator.VoteId, Now))).Failure);
        Assert.IsTrue((await fixture.Votes.CastAsync(AnoVetoCoordinator.VoteId, Normal, "one", Now)).Accepted);
        Assert.IsTrue((await fixture.Run(fixture.Votes.CancelAsync(Host, AnoVetoCoordinator.VoteId, Now))).Accepted);
        Assert.IsFalse((await new VoteService(new DenyPermissions()).CreateAsync(Host, Definition(), Now)).Accepted);
        Assert.AreEqual(VoteOperationFailure.Forbidden,
            (await fixture.Run(fixture.Votes.CreateAsync(Normal, Definition(), Now))).Failure);
    }

    [TestMethod]
    public async Task QueuedManagement_RechecksRevocationReconnectAndUnloadBeforeMutation()
    {
        var fixture = await Fixture.CreateAsync();
        var create = fixture.Votes.CreateAsync(Host, Definition(), Now);
        fixture.Allowed = false;
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(create)).Failure);
        Assert.IsFalse(fixture.Votes.TryGet(AnoVetoCoordinator.VoteId, out _));
        fixture.Allowed = true;
        create = fixture.Votes.CreateAsync(Host, Definition(), Now);
        await fixture.Connect(Host);
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(create)).Failure);
        Assert.IsTrue((await fixture.Run(fixture.Votes.CreateAsync(Host, Definition(), Now))).Accepted);
        var close = fixture.Votes.CloseAsync(Host, AnoVetoCoordinator.VoteId, Now);
        fixture.Allowed = false;
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(close)).Failure);
        Assert.IsTrue(fixture.Votes.TryGet(AnoVetoCoordinator.VoteId, out var snapshot));
        Assert.AreEqual(VoteState.Open, snapshot!.State);
        fixture.Allowed = true;
        var cancel = fixture.Votes.CancelAsync(Host, AnoVetoCoordinator.VoteId, Now);
        fixture.Active = false;
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(cancel)).Failure);
    }

    [TestMethod]
    public async Task Scope_CancellationAndLookupFailureCannotCreateVotes()
    {
        var fixture = await Fixture.CreateAsync();
        var other = new VoteDefinition(new VoteId("other.vote"), "Other", [new VoteOption("one", "One")],
            [Host], new VotePolicy(TimeSpan.FromSeconds(30), 1, VoteTieBreakPolicy.OptionOrder));
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(fixture.Votes.CreateAsync(Host, other, Now))).Failure);
        using var cancellation = new CancellationTokenSource();
        var create = fixture.Votes.CreateAsync(Host, Definition(), Now, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await fixture.Run(create));
        fixture.ThrowLookup = true;
        Assert.AreEqual(VoteOperationFailure.Forbidden, (await fixture.Run(fixture.Votes.CreateAsync(Host, Definition(), Now))).Failure);
        Assert.IsFalse(fixture.Votes.TryGet(AnoVetoCoordinator.VoteId, out _));
    }

    private static VoteDefinition Definition() => new(AnoVetoCoordinator.VoteId, "Veto",
        [new VoteOption("one", "One"), new VoteOption("two", "Two")], [Host, Normal],
        new VotePolicy(TimeSpan.FromSeconds(30), 1, VoteTieBreakPolicy.OptionOrder));

    private sealed class Fixture
    {
        public PlayerRegistry Players { get; } = new(new AnoEventBus());
        public Queue<Action> Queue { get; } = new();
        public bool Allowed { get; set; } = true;
        public bool Active { get; set; } = true;
        public bool ThrowLookup { get; set; }
        public SessionBoundVetoVoteService Votes { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Connect(Host);
            await fixture.Connect(Normal);
            fixture.Votes = new SessionBoundVetoVoteService(fixture.Players, fixture.Queue.Enqueue,
                player => fixture.ThrowLookup ? throw new InvalidOperationException() : fixture.Allowed && player.Id == Host,
                () => fixture.Active);
            return fixture;
        }

        public ValueTask<PlayerSnapshot> Connect(PlayerId id) => Players.ConnectAsync(
            new PlayerConnection(id, "Player", PlayerTeam.Terrorist, true, Now));

        public async Task<VoteOperationResult> Run(ValueTask<VoteOperationResult> result)
        {
            while (Queue.TryDequeue(out var action)) action();
            return await result;
        }
    }

    private sealed class DenyPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}
