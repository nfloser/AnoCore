using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentEventCommandControllerTests
{
    [TestMethod]
    public async Task PlayoffCommandRejectsUnauthorizedAndInvalidResultsBeforeAudit()
    {
        var repository = new Repository();
        var audit = new Audit();
        var registry = new CommandRegistry(new DenyAll());
        using var controller = new TournamentEventCommandController(new Config(), registry, repository, audit);
        var rejected = await registry.ExecuteAsync("!anotournamenteventplayoffresult r1-m1 team-2 1 0", new PlayerId(123));
        Assert.IsFalse(rejected.Success);
        Assert.AreEqual(0, audit.Actions.Count);
        var invalid = await registry.ExecuteAsync("!anotournamenteventplayoffresult r2-m1 team-1 1 0", null);
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(0, audit.Actions.Count);
        Assert.HasCount(0, repository.Current.PlayoffResults);
        Assert.IsTrue((await registry.ExecuteAsync("!anotournamenteventbracket", new PlayerId(123))).Success);
    }

    [TestMethod]
    public async Task PlayoffCommandAuditsAndAdvancesButNeverDuplicatesACommittedResult()
    {
        var repository = new Repository();
        var audit = new Audit();
        var registry = new CommandRegistry(new DenyAll());
        using var controller = new TournamentEventCommandController(new Config(), registry, repository, audit);
        var command = "!anotournamenteventplayoffresult r1-m1 team-2 1 0";
        audit.FailCompletion = true;
        var saved = await registry.ExecuteAsync(command, null);
        Assert.IsFalse(saved.Success);
        StringAssert.Contains(saved.Message!, "Result saved");
        Assert.HasCount(1, repository.Current.PlayoffResults);
        var duplicate = await registry.ExecuteAsync(command, null);
        Assert.IsFalse(duplicate.Success);
        Assert.HasCount(1, repository.Current.PlayoffResults);
        var status = await registry.ExecuteAsync("!anotournamenteventbracket", null);
        StringAssert.Contains(status.Message!, "r2-m1: team-1 vs team-2 [ready]");
    }

    private sealed class Repository : ITournamentEventRepository
    {
        public TournamentStoredEvent Current { get; private set; } = new(Definition(), [], 1);
        public ValueTask<TournamentStoredEvent?> LoadActiveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<TournamentStoredEvent?>(Current);
        public ValueTask<TournamentStoredEvent?> LoadAsync(string eventId, CancellationToken cancellationToken = default) => LoadActiveAsync(cancellationToken);
        public ValueTask<TournamentStoredEvent> CreateAsync(TournamentEventDefinition definition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TournamentStoredEvent> SaveAsync(string eventId, long expectedRevision, TournamentEventDefinition definition, IReadOnlyCollection<TournamentGroupResult> confirmedResults, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ActivateAsync(string eventId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TournamentStoredEvent> SavePlayoffsAsync(string eventId, long expectedRevision, IReadOnlyCollection<TournamentBracketResult> results, CancellationToken cancellationToken = default)
        {
            if (expectedRevision != Current.Revision) throw new TournamentEventConcurrencyException(eventId, expectedRevision);
            Current = Current with { PlayoffResults = results.ToArray(), Revision = expectedRevision + 1 };
            return ValueTask.FromResult(Current);
        }
    }

    private static TournamentEventDefinition Definition() => new()
    {
        Id = "event",
        Name = "Event",
        Teams = Enumerable.Range(1, 3).Select(i => new TournamentEventTeamDefinition
        {
            Id = $"team-{i}",
            Name = $"Team {i}",
            Tag = $"T{i}",
            InitialSeed = i,
            Starters = Enumerable.Range(0, 5).Select(j => (ulong)(i * 10 + j + 1)).ToList(),
            CaptainSteamId = (ulong)(i * 10 + 1),
        }).ToList(),
    };

    private sealed class Config : IConfigStore
    {
        public ValueTask<T> LoadAsync<T>(string name, Func<T> createDefault, Func<T, IReadOnlyCollection<string>>? validate = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(createDefault());
        public ValueTask SaveAsync<T>(string name, T value, Func<T, IReadOnlyCollection<string>>? validate = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class DenyAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
    private sealed class Audit : IAdminAuditService
    {
        public List<string> Actions { get; } = [];
        public bool FailCompletion { get; set; }
        public ValueTask<AdminAuditEntry> RecordAsync(AdminActionId action, PlayerId? actorId, PlayerId? targetId, string reason, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
        {
            if (FailCompletion && action.Value == "tournament.playoff-result") throw new InvalidOperationException("Audit unavailable.");
            Actions.Add(action.Value);
            return ValueTask.FromResult(new AdminAuditEntry(Guid.NewGuid(), action, actorId, targetId, reason, occurredAtUtc));
        }
        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>([]);
        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>([]);
    }
}
