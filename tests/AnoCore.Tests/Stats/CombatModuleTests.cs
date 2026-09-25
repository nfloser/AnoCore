using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatModuleTests
{
    private static readonly PlayerId Victim = new(76561198000012301);
    private static readonly PlayerId Attacker = new(76561198000012302);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RecordAndSelfCommand_UseSharedRepositoryAndDisposeRegistration()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var registry = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository();
        var module = new CombatModule(registry, players, repository);
        var death = new CombatDeath(Guid.NewGuid(), Victim, Attacker, null, Now);
        await module.RecordAsync(death);
        Assert.AreSame(death, repository.LastRecorded);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await registry.ExecuteAsync("!anokda", null)).FailureReason);
        await players.ConnectAsync(new PlayerConnection(Attacker, "Attacker",
            PlayerTeam.Terrorist, true, Now));
        var result = await registry.ExecuteAsync("!anokda", Attacker);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "1 kill");
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anokda", Attacker)).FailureReason);
    }

    [TestMethod]
    public void StableDeathId_ReplaysSameNativeTickAndSeparatesDifferentVictims()
    {
        var first = CombatEventIdentity.Create("server-process", "de_dust2", 1234, 25, Victim);
        Assert.AreEqual(first,
            CombatEventIdentity.Create("server-process", "de_dust2", 1234, 25, Victim));
        Assert.AreNotEqual(first,
            CombatEventIdentity.Create("server-process", "de_dust2", 1234, 25, Attacker));
        Assert.AreNotEqual(first,
            CombatEventIdentity.Create("server-process", "de_dust2", 1234, 26, Victim));
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FakeRepository : ICombatRepository
    {
        public CombatDeath? LastRecorded { get; private set; }
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
        {
            LastRecorded = death;
            return ValueTask.CompletedTask;
        }
        public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CombatTotals(1, 2, 3));
    }
}
