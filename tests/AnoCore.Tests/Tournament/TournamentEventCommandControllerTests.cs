using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;

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

    [TestMethod]
    public async Task PreviewAndPinnedImportRejectStaleConfigurationWithoutPublishing()
    {
        var repository = new Repository(); var config = new Config(); var audit = new Audit(); var commands = new CommandRegistry(new DenyAll());
        using var controller = new TournamentEventCommandController(config, commands, repository, audit);
        var preview = await commands.ExecuteAsync("!anotournamenteventpreview", null);
        Assert.IsTrue(preview.Success); StringAssert.Contains(preview.Message!, "Preview only");
        Assert.AreEqual(0, repository.Writes); Assert.AreEqual(0, config.Saves);
        var hash = TournamentEventImportConfiguration.Fingerprint(config.Document);
        config.Document.Event.Name = "Changed after preview";
        Assert.IsFalse((await commands.ExecuteAsync("!anotournamenteventload 1 " + hash, null)).Success);
        hash = TournamentEventImportConfiguration.Fingerprint(config.Document);
        Assert.IsFalse((await commands.ExecuteAsync("!anotournamenteventload 0 " + hash, null)).Success);
        Assert.AreEqual(0, repository.Writes);
        Assert.IsFalse((await commands.ExecuteAsync("!anotournamenteventpreset Swiss", new PlayerId(123))).Success);
        Assert.AreEqual(0, config.Saves);
    }
    [TestMethod]
    public async Task MenuConsumesConfirmationAndRejectsChangedConfigurationOrRevokedPermissions()
    {
        var bus = new AnoEventBus(); var players = new PlayerRegistry(bus); var admin = new PlayerId(123);
        await players.ConnectAsync(new(admin, "Admin", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var permission = new MutablePermission(); var commands = new CommandRegistry(permission); var menus = new MenuService();
        var repository = new Repository(); var config = new Config();
        using var controller = new TournamentEventCommandController(config, commands, repository, new Audit());
        using var module = new TournamentAdminMenuModule(commands, players, menus, config, repository, bus);
        await commands.ExecuteAsync("!anotournamentadmin", admin);
        await menus.SelectAsync(admin, "publish"); menus.TryGetOpenMenu(admin, out var confirm);
        config.Document.Event.Name = "Changed";
        await menus.SelectAsync(admin, confirm!, "confirm");
        await menus.SelectAsync(admin, confirm!, "confirm");
        Assert.AreEqual(0, repository.Writes);
        await commands.ExecuteAsync("!anotournamentadmin", admin); await menus.SelectAsync(admin, "publish");
        permission.Allowed = false; await menus.SelectAsync(admin, "confirm"); Assert.AreEqual(0, repository.Writes);
        permission.Allowed = true;
        await commands.ExecuteAsync("!anotournamentadmin", admin); await menus.SelectAsync(admin, "publish");
        menus.TryGetOpenMenu(admin, out confirm);
        await menus.SelectAsync(admin, confirm!, "confirm"); await menus.SelectAsync(admin, confirm!, "confirm");
        Assert.AreEqual(1, repository.Writes);
    }
    private sealed class MutablePermission : IPermissionEvaluator
    {
        public bool Allowed = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken token = default) => ValueTask.FromResult(Allowed);
    }

    [TestMethod]
    public async Task NextFixturesIsPublicReadOnlyAndUsesDeterministicMatchIds()
    {
        var repository = new Repository();
        var audit = new Audit();
        var registry = new CommandRegistry(new DenyAll());
        using var controller = new TournamentEventCommandController(new Config(), registry, repository, audit);

        var first = await registry.ExecuteAsync("!anotournamenteventnext", new PlayerId(123));
        Assert.IsTrue(first.Success);
        StringAssert.Contains(first.Message!, "1 / 1");
        StringAssert.Contains(first.Message!, "group");
        StringAssert.Contains(first.Message!, "Team");
        var expected = TournamentFixtureMatchPlanner.Plan(repository.Current.Definition, [], [])[0].Match.MatchId;
        StringAssert.Contains(first.Message!, expected);

        var invalid = await registry.ExecuteAsync("!anotournamenteventnext 0", new PlayerId(123));
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(0, audit.Actions.Count);
        Assert.HasCount(0, repository.Current.ConfirmedResults);
    }

    [TestMethod]
    public async Task FixturePreviewUsesTheSameConfiguredBestOfAsMatchZyClaims()
    {
        var registry = new CommandRegistry(new DenyAll());
        using var controller = new TournamentEventCommandController(
            new Config(), registry, new Repository(), new Audit(),
            new TournamentMatchZyConfiguration
            {
                Enabled = true,
                ResultBaseUrl = "https://example.org",
                DefaultGroupBestOf = 3,
                DefaultPlayoffBestOf = 5,
            });
        var preview = await registry.ExecuteAsync("!anotournamenteventnext", new PlayerId(123));
        Assert.IsTrue(preview.Success);
        StringAssert.Contains(preview.Message!, "BO3");
    }

    private sealed class Repository : ITournamentEventRepository
    {
        public int Writes;
        public TournamentStoredEvent Current { get; private set; } = new(Definition(), [], 1);
        public ValueTask<TournamentStoredEvent?> LoadActiveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<TournamentStoredEvent?>(Current);
        public ValueTask<TournamentStoredEvent?> LoadAsync(string eventId, CancellationToken cancellationToken = default) => LoadActiveAsync(cancellationToken);
        public ValueTask<TournamentStoredEvent> CreateAsync(TournamentEventDefinition definition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TournamentStoredEvent> SaveAsync(string eventId, long expectedRevision, TournamentEventDefinition definition, IReadOnlyCollection<TournamentGroupResult> confirmedResults, CancellationToken cancellationToken = default)
        {
            if (expectedRevision != Current.Revision) throw new TournamentEventConcurrencyException(eventId, expectedRevision);
            Writes++; Current = new(definition, confirmedResults.ToArray(), expectedRevision + 1) { PlayoffResults = Current.PlayoffResults }; return ValueTask.FromResult(Current);
        }
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
        public TournamentEventImportConfiguration Document = new() { Enabled = true, Event = Definition() };
        public int Saves;
        public ValueTask<T> LoadAsync<T>(string name, Func<T> createDefault, Func<T, IReadOnlyCollection<string>>? validate = null, CancellationToken cancellationToken = default) => ValueTask.FromResult((T)(object)Document);
        public ValueTask SaveAsync<T>(string name, T value, Func<T, IReadOnlyCollection<string>>? validate = null, CancellationToken cancellationToken = default) { Saves++; Document = (TournamentEventImportConfiguration)(object)value!; return ValueTask.CompletedTask; }
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
