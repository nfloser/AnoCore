using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ProtectedServerControlExecutorTests
{
    private static readonly PlayerId ActorId = new(76561198000016801);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 17, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void PolicyRequiresExactAllowListAndKeepsBuiltInDenies()
    {
        var policy = ProtectedServerControlPolicy.Create(
            new ProtectedServerControlConfiguration
            {
                AllowedConVars = ["mp_friendlyfire", "rcon_password"],
                AllowedServerCommands = ["mp_restartgame", "quit", "exec"],
            });

        Assert.IsTrue(policy.AllowsConVar("mp_friendlyfire"));
        Assert.IsFalse(policy.AllowsConVar("mp_friendly"));
        Assert.IsFalse(policy.AllowsConVar("rcon_password"));

        Assert.IsTrue(policy.AllowsServerCommand("mp_restartgame"));
        Assert.IsFalse(policy.AllowsServerCommand("mp_restart"));
        Assert.IsFalse(policy.AllowsServerCommand("quit"));
        Assert.IsFalse(policy.AllowsServerCommand("exec"));
    }

    [TestMethod]
    public void PolicyTreatsExplicitNullAllowlistsAsEmpty()
    {
        var configuration = new ProtectedServerControlConfiguration
        {
            AllowedConVars = null,
            AllowedServerCommands = null,
        };

        var policy = ProtectedServerControlPolicy.Create(configuration);

        Assert.IsFalse(policy.AllowsConVar("mp_friendlyfire"));
        Assert.IsFalse(policy.AllowsServerCommand("mp_restartgame"));
    }

    [TestMethod]
    public async Task SetCVarRejectsControlCharactersAndUnlistedNamesBeforeAudit()
    {
        var policy = Policy();
        var transport = new RecordingTransport();
        var audit = new RecordingAudit();
        var executor = CreateExecutor(policy, transport, audit);

        var injection = await executor.SetCVarAsync(
            ActorId,
            "mp_friendlyfire",
            "1; quit");
        var unknown = await executor.SetCVarAsync(
            ActorId,
            "sv_cheats",
            "1");

        Assert.IsFalse(injection.Success);
        Assert.IsFalse(unknown.Success);
        Assert.AreEqual(0, transport.CVarWrites.Count);
        Assert.AreEqual(0, audit.Entries.Count);
    }

    [TestMethod]
    public async Task SetCVarAuditsRedactedIntentAndNeverStoresValue()
    {
        var policy = Policy();
        var transport = new RecordingTransport();
        var audit = new RecordingAudit();
        var executor = CreateExecutor(policy, transport, audit);

        const string secretLikeValue = "super-secret-value";
        var result = await executor.SetCVarAsync(
            ActorId,
            "mp_friendlyfire",
            secretLikeValue);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(
            ("mp_friendlyfire", secretLikeValue),
            transport.CVarWrites.Single());
        Assert.AreEqual(1, audit.Entries.Count);
        Assert.AreEqual(
            "extended.cvar.attempt",
            audit.Entries[0].Action.Value);
        StringAssert.Contains(audit.Entries[0].Reason, "mp_friendlyfire");
        Assert.IsFalse(
            audit.Entries[0].Reason.Contains(
                secretLikeValue,
                StringComparison.Ordinal));
        StringAssert.Contains(audit.Entries[0].Reason, "<redacted>");
    }

    [TestMethod]
    public async Task ServerCommandRejectsSeparatorsAndExecutesOnlyAllowListedCommand()
    {
        var policy = Policy();
        var transport = new RecordingTransport();
        var audit = new RecordingAudit();
        var executor = CreateExecutor(policy, transport, audit);

        var rejected = await executor.ExecuteServerCommandAsync(
            ActorId,
            "mp_restartgame",
            "1; quit");
        var accepted = await executor.ExecuteServerCommandAsync(
            ActorId,
            "mp_restartgame",
            "1");

        Assert.IsFalse(rejected.Success);
        Assert.IsTrue(accepted.Success);
        Assert.AreEqual(
            ("mp_restartgame", "1"),
            transport.ServerCommands.Single());
        Assert.AreEqual(
            "extended.server-command.attempt",
            audit.Entries.Single().Action.Value);
    }

    [TestMethod]
    public async Task DefenseInDepthPermissionCheckBlocksDirectExecutorUse()
    {
        var transport = new RecordingTransport();
        var audit = new RecordingAudit();
        var executor = new ProtectedServerControlExecutor(
            Policy(),
            new DenyAllPermissions(),
            audit,
            transport,
            new FixedTimeProvider(Now));

        var result = await executor.SetCVarAsync(
            ActorId,
            "mp_friendlyfire",
            "1");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, audit.Entries.Count);
        Assert.AreEqual(0, transport.CVarWrites.Count);
    }

    [TestMethod]
    public async Task ServerConsoleRemainsAllowedForConfiguredControls()
    {
        var transport = new RecordingTransport();
        var executor = CreateExecutor(
            Policy(),
            transport,
            new RecordingAudit());

        var result = await executor.SetCVarAsync(
            null,
            "mp_friendlyfire",
            "0");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, transport.CVarWrites.Count);
    }

    [TestMethod]
    public async Task SameIpOutputIsBoundedAndContainsNoRawAddress()
    {
        var transport = new RecordingTransport
        {
            SameIpGroups =
            [
                new SameIpPlayerGroup(
                    "network-a1b2c3d4",
                    [
                        new SameIpPlayer(ActorId, "Alpha"),
                        new SameIpPlayer(new PlayerId(76561198000016802), "Beta"),
                    ]),
                new SameIpPlayerGroup(
                    "network-deadbeef",
                    [
                        new SameIpPlayer(new PlayerId(76561198000016803), "Gamma"),
                        new SameIpPlayer(new PlayerId(76561198000016804), "Delta"),
                    ]),
            ],
        };

        var executor = CreateExecutor(
            Policy(),
            transport,
            new RecordingAudit());

        var result = await executor.ListSameIpAsync(ActorId);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message, "Alpha");
        StringAssert.Contains(result.Message, "Beta");
        StringAssert.Contains(result.Message, "network-a1b2c3d4");
        Assert.IsFalse(result.Message.Contains("192.168.", StringComparison.Ordinal));
        Assert.IsTrue(result.Message.Length <= ProtectedServerControlExecutor.MaxOutputLength);
    }

    private static ProtectedServerControlPolicy Policy()
        => ProtectedServerControlPolicy.Create(
            new ProtectedServerControlConfiguration
            {
                AllowedConVars = ["mp_friendlyfire"],
                AllowedServerCommands = ["mp_restartgame"],
            });

    private static ProtectedServerControlExecutor CreateExecutor(
        ProtectedServerControlPolicy policy,
        RecordingTransport transport,
        RecordingAudit audit)
        => new(
            policy,
            new AllowAllPermissions(),
            audit,
            transport,
            new FixedTimeProvider(Now));

    private sealed class RecordingTransport : IProtectedServerControlTransport
    {
        public List<(string Name, string Value)> CVarWrites { get; } = [];
        public List<(string Command, string Arguments)> ServerCommands { get; } = [];
        public IReadOnlyList<SameIpPlayerGroup> SameIpGroups { get; set; } = [];

        public ValueTask<bool> SetCVarAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            CVarWrites.Add((name, value));
            return ValueTask.FromResult(true);
        }

        public ValueTask ExecuteServerCommandAsync(
            string command,
            string arguments,
            CancellationToken cancellationToken = default)
        {
            ServerCommands.Add((command, arguments));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<SameIpPlayerGroup>> GetSameIpGroupsAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SameIpGroups);
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = [];

        public ValueTask<AdminAuditEntry> RecordAsync(
            AdminActionId action,
            PlayerId? actorId,
            PlayerId? targetId,
            string reason,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            var entry = new AdminAuditEntry(
                Guid.NewGuid(),
                action,
                actorId,
                targetId,
                reason,
                occurredAtUtc);
            Entries.Add(entry);
            return ValueTask.FromResult(entry);
        }

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
            PlayerId targetId,
            int limit = 100,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
