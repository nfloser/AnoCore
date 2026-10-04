using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentCommandControllerTests
{
    private static readonly PlayerId A1 = new(76561198000197001);
    private static readonly PlayerId A2 = new(76561198000197002);
    private static readonly PlayerId B1 = new(76561198000197011);
    private static readonly PlayerId B2 = new(76561198000197012);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task LoadAndStatus_PersistThenPublishActiveMatch()
    {
        var fixture = await Fixture.CreateAsync();
        using var controller = fixture.Controller;

        var load = await fixture.Commands.ExecuteAsync("!anotournamentload", null);
        var status = await fixture.Commands.ExecuteAsync("!anotournamentstatus", null);

        Assert.IsTrue(load.Success);
        Assert.IsTrue(status.Success);
        StringAssert.Contains(status.Message!, "Setup");
        Assert.IsNotNull(fixture.Runtime.CurrentSession);
        Assert.IsNotNull(await fixture.Repository.LoadActiveAsync());
        Assert.AreEqual(
            Guid.Parse(TournamentMatchDefinitionTests.ValidDefinition().MatchId),
            fixture.Policies.Read().Policy?.MatchId);
        CollectionAssert.AreEqual(
            new[] { "tournament.load.requested", "tournament.load" },
            fixture.Audit.Actions.ToArray());
    }

    [TestMethod]
    public async Task PlayerPermissionDenial_HappensBeforeLoadMutationAndAudit()
    {
        var fixture = await Fixture.CreateAsync(permissions: new DenyAll());
        using var controller = fixture.Controller;
        await fixture.Players.ConnectAsync(new PlayerConnection(
            A1, "A1", PlayerTeam.Terrorist, true, Now));

        var result = await fixture.Commands.ExecuteAsync("!anotournamentload", A1);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsNull(fixture.Runtime.CurrentSession);
        Assert.AreEqual(0, fixture.Audit.Actions.Count);
        Assert.IsNull(await fixture.Repository.LoadActiveAsync());
    }

    [TestMethod]
    public async Task FailedSave_DoesNotPublishCandidateState()
    {
        var fixture = await Fixture.CreateAsync();
        using var controller = fixture.Controller;
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentload", null)).Success);
        fixture.Repository.FailSave = true;

        var result = await fixture.Commands.ExecuteAsync("!anotournamentreadyopen", null);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.HandlerFailed, result.FailureReason);
        Assert.AreEqual(TournamentMatchState.Setup, fixture.Runtime.CurrentSession?.Machine.State);
        var stored = await fixture.Repository.LoadActiveAsync();
        Assert.AreEqual(TournamentMatchState.Setup, stored?.Snapshot.State);
        CollectionAssert.Contains(
            fixture.Audit.Actions.ToArray(), "tournament.ready-open.requested");
        Assert.IsFalse(fixture.Audit.Actions.Contains("tournament.ready-open"));
    }

    [TestMethod]
    public async Task CompletionAuditFailure_StillPublishesPersistedState()
    {
        var fixture = await Fixture.CreateAsync();
        using var controller = fixture.Controller;
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentload", null)).Success);
        fixture.Audit.FailAction = "tournament.ready-open";

        var result = await fixture.Commands.ExecuteAsync("!anotournamentreadyopen", null);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.HandlerFailed, result.FailureReason);
        Assert.AreEqual(TournamentMatchState.Ready, fixture.Runtime.CurrentSession?.Machine.State);
        Assert.AreEqual(
            TournamentMatchState.Ready,
            (await fixture.Repository.LoadActiveAsync())?.Snapshot.State);
    }

    [TestMethod]
    public async Task ReadyMapsKnifeAndSide_RequireRosterAndWinningCaptain()
    {
        var fixture = await Fixture.CreateAsync();
        using var controller = fixture.Controller;
        await fixture.ConnectRosterAsync();

        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentload", null)).Success);
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentreadyopen", null)).Success);
        foreach (var player in new[] { A1, A2, B1, B2 })
            Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anoready", player)).Success);

        Assert.IsTrue((await fixture.Commands.ExecuteAsync(
            "!anotournamentmaps de_dust2,de_nuke,de_inferno", null)).Success);
        Assert.IsTrue((await fixture.Commands.ExecuteAsync(
            "!anotournamentknife a", null)).Success);

        var denied = await fixture.Commands.ExecuteAsync("!anotournamentside ct", A2);
        Assert.IsFalse(denied.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, denied.FailureReason);

        var accepted = await fixture.Commands.ExecuteAsync("!anotournamentside ct", A1);
        Assert.IsTrue(accepted.Success);
        Assert.AreEqual(TournamentMatchState.Live, fixture.Runtime.CurrentSession?.Machine.State);
        Assert.IsTrue(fixture.Runtime.TryGetAssignedSide(A1, out var aSide));
        Assert.AreEqual(PlayerTeam.CounterTerrorist, aSide);
    }

    [TestMethod]
    public async Task ConcurrentReadyCommands_AreSerializedThroughPersistence()
    {
        var fixture = await Fixture.CreateAsync();
        using var controller = fixture.Controller;
        await fixture.ConnectRosterAsync();
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentload", null)).Success);
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentreadyopen", null)).Success);
        fixture.Repository.SaveDelay = TimeSpan.FromMilliseconds(20);

        var first = fixture.Commands.ExecuteAsync("!anoready", A1).AsTask();
        var second = fixture.Commands.ExecuteAsync("!anoready", A2).AsTask();
        await Task.WhenAll(first, second);

        Assert.IsTrue(first.Result.Success);
        Assert.IsTrue(second.Result.Success);
        Assert.AreEqual(1, fixture.Repository.MaxConcurrentSave);
        var ready = fixture.Runtime.CurrentSession!.Machine.Snapshot().ReadyPlayers;
        Assert.IsTrue(ready.Contains(A1));
        Assert.IsTrue(ready.Contains(A2));
    }

    [TestMethod]
    public async Task Bo1MapCompletion_DeactivatesButKeepsFinalSnapshot()
    {
        var definition = TournamentMatchDefinitionTests.ValidDefinition();
        definition.BestOf = 1;
        definition.KnifeRound = false;
        var fixture = await Fixture.CreateAsync(definition);
        using var controller = fixture.Controller;
        await fixture.ConnectRosterAsync();

        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentload", null)).Success);
        Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anotournamentreadyopen", null)).Success);
        foreach (var player in new[] { A1, A2, B1, B2 })
            Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anoready", player)).Success);
        Assert.IsTrue((await fixture.Commands.ExecuteAsync(
            "!anotournamentmaps de_mirage", null)).Success);

        var result = await fixture.Commands.ExecuteAsync("!anotournamentmapwin a", null);

        Assert.IsTrue(result.Success);
        Assert.IsNull(fixture.Runtime.CurrentSession);
        Assert.IsNull(await fixture.Repository.LoadActiveAsync());
        Assert.IsNull(fixture.Policies.Read().Policy);
        var stored = await fixture.Repository.LoadAsync(Guid.Parse(definition.MatchId));
        Assert.IsNotNull(stored);
        Assert.AreEqual(TournamentMatchState.Completed, stored.Snapshot.State);
        Assert.AreEqual(1, stored.Snapshot.TeamAMaps);
    }

    [TestMethod]
    public async Task Dispose_UnregistersTournamentCommands()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Controller.Dispose();

        var result = await fixture.Commands.ExecuteAsync("!anotournamentstatus", null);

        Assert.AreEqual(CommandFailureReason.NotFound, result.FailureReason);
    }

    private sealed class Fixture
    {
        private Fixture(
            PlayerRegistry players,
            CommandRegistry commands,
            MemoryRepository repository,
            TournamentMatchRuntime runtime,
            RecordingAudit audit,
            TournamentSpectatorPolicySource policies,
            TournamentCommandController controller)
        {
            Players = players;
            Commands = commands;
            Repository = repository;
            Runtime = runtime;
            Audit = audit;
            Policies = policies;
            Controller = controller;
        }

        public PlayerRegistry Players { get; }
        public CommandRegistry Commands { get; }
        public MemoryRepository Repository { get; }
        public TournamentMatchRuntime Runtime { get; }
        public RecordingAudit Audit { get; }
        public TournamentSpectatorPolicySource Policies { get; }
        public TournamentCommandController Controller { get; }

        public static async Task<Fixture> CreateAsync(
            TournamentMatchDefinition? definition = null,
            IPermissionEvaluator? permissions = null)
        {
            var events = new AnoEventBus();
            var players = new PlayerRegistry(events);
            var commands = new CommandRegistry(permissions ?? new AllowAll());
            var repository = new MemoryRepository();
            var recovery = new TournamentRecoveryService(repository);
            var runtime = await TournamentMatchRuntime.CreateAsync(recovery);
            var audit = new RecordingAudit();
            var policies = new TournamentSpectatorPolicySource();
            var controller = new TournamentCommandController(
                new FixedConfig(definition ?? TournamentMatchDefinitionTests.ValidDefinition()),
                commands,
                players,
                recovery,
                runtime,
                audit,
                time: new FixedTime(Now),
                spectatorPolicies: policies);
            return new Fixture(
                players, commands, repository, runtime, audit, policies, controller);
        }

        public async Task ConnectRosterAsync()
        {
            await Players.ConnectAsync(new PlayerConnection(
                A1, "A1", PlayerTeam.Terrorist, true, Now));
            await Players.ConnectAsync(new PlayerConnection(
                A2, "A2", PlayerTeam.Terrorist, true, Now));
            await Players.ConnectAsync(new PlayerConnection(
                B1, "B1", PlayerTeam.CounterTerrorist, true, Now));
            await Players.ConnectAsync(new PlayerConnection(
                B2, "B2", PlayerTeam.CounterTerrorist, true, Now));
        }
    }

    private sealed class MemoryRepository : ITournamentMatchRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, TournamentStoredMatch> _matches = [];
        private Guid? _active;
        private int _concurrentSave;

        public bool FailSave { get; set; }
        public TimeSpan SaveDelay { get; set; }
        public int MaxConcurrentSave { get; private set; }

        public ValueTask<TournamentStoredMatch> StoreAsync(
            TournamentMatchConfiguration configuration,
            TournamentRecoverySnapshot snapshot,
            bool makeActive,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var revision = _matches.TryGetValue(configuration.MatchId, out var existing)
                    ? existing.Revision + 1 : 1;
                var stored = new TournamentStoredMatch(configuration, snapshot, revision);
                _matches[configuration.MatchId] = stored;
                if (makeActive) _active = configuration.MatchId;
                return ValueTask.FromResult(stored);
            }
        }

        public ValueTask<TournamentStoredMatch?> LoadAsync(
            Guid matchId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return ValueTask.FromResult(
                    _matches.TryGetValue(matchId, out var value) ? value : null);
            }
        }

        public ValueTask<TournamentStoredMatch?> LoadActiveAsync(
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return ValueTask.FromResult(
                    _active is { } id && _matches.TryGetValue(id, out var value)
                        ? value : null);
            }
        }

        public async ValueTask<TournamentStoredMatch> SaveSnapshotAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            var concurrent = Interlocked.Increment(ref _concurrentSave);
            MaxConcurrentSave = Math.Max(MaxConcurrentSave, concurrent);
            try
            {
                if (SaveDelay > TimeSpan.Zero)
                    await Task.Delay(SaveDelay, cancellationToken);
                if (FailSave)
                    throw new InvalidOperationException("forced save failure");

                lock (_gate)
                {
                    var current = _matches[matchId];
                    if (current.Revision != expectedRevision)
                        throw new TournamentConcurrencyException(
                            matchId, expectedRevision, current.Revision);
                    var stored = new TournamentStoredMatch(
                        current.Configuration, snapshot, expectedRevision + 1);
                    _matches[matchId] = stored;
                    return stored;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentSave);
            }
        }

        public async ValueTask<TournamentStoredMatch> DeactivateAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            var stored = await SaveSnapshotAsync(
                matchId, expectedRevision, snapshot, cancellationToken);
            lock (_gate)
            {
                if (_active == matchId) _active = null;
            }
            return stored;
        }
    }

    private sealed class FixedConfig(TournamentMatchDefinition value) : IConfigStore
    {
        public ValueTask<T> LoadAsync<T>(
            string name,
            Func<T> createDefault,
            Func<T, IReadOnlyCollection<string>>? validate = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value is not T typed)
                throw new InvalidOperationException("Unexpected configuration type.");
            var errors = validate?.Invoke(typed) ?? [];
            if (errors.Count != 0)
                throw new InvalidOperationException(string.Join(" ", errors));
            return ValueTask.FromResult(typed);
        }

        public ValueTask SaveAsync<T>(
            string name,
            T value,
            Func<T, IReadOnlyCollection<string>>? validate = null,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<string> Actions { get; } = [];
        public string? FailAction { get; set; }

        public ValueTask<AdminAuditEntry> RecordAsync(
            AdminActionId action,
            PlayerId? actorId,
            PlayerId? targetId,
            string reason,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (action.Value == FailAction)
                throw new InvalidOperationException("forced audit failure");
            Actions.Add(action.Value);
            return ValueTask.FromResult(new AdminAuditEntry(
                Guid.NewGuid(), action, actorId, targetId, reason, occurredAtUtc));
        }

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>([]);

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
            PlayerId targetId,
            int limit = 100,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>([]);
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId id,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId id,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
