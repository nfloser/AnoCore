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

    [TestMethod]
    public void StableDetailId_SeparatesTypeParticipantsAndPayload()
    {
        var first = CombatEventIdentity.CreateDetail(
            "server-process", "de_dust2", 1234, 25, "player_hurt",
            Victim, Attacker, "ak47|1|40|5");
        Assert.AreEqual(first, CombatEventIdentity.CreateDetail(
            "server-process", "de_dust2", 1234, 25, "player_hurt",
            Victim, Attacker, "ak47|1|40|5"));
        Assert.AreNotEqual(first, CombatEventIdentity.CreateDetail(
            "server-process", "de_dust2", 1234, 25, "weapon_fire",
            Victim, Attacker, "ak47|1|40|5"));
        Assert.AreNotEqual(first, CombatEventIdentity.CreateDetail(
            "server-process", "de_dust2", 1234, 25, "player_hurt",
            Victim, null, "ak47|1|40|5"));
        Assert.AreNotEqual(first, CombatEventIdentity.CreateDetail(
            "server-process", "de_dust2", 1234, 25, "player_hurt",
            Victim, Attacker, "ak47|2|40|5"));
    }

    [TestMethod]
    public async Task DetailEvents_UseOptionalRepositoryAndLegacyRepositoryNoops()
    {
        var registry = new CommandRegistry(new AllowAll());
        var players = new PlayerRegistry(new AnoEventBus());
        var details = new DetailRepository();
        using (var module = new CombatModule(registry, players, details))
        {
            var fire = new CombatWeaponFireEvent(
                Guid.NewGuid(), Attacker, Now, "de_dust2", "ak47");
            var damage = new CombatDamageEvent(
                Guid.NewGuid(), Victim, Attacker, Now, "de_dust2", "ak47", 1, 25, 4);
            await module.RecordWeaponFireAsync(fire);
            await module.RecordDamageAsync(damage);
            Assert.AreSame(fire, details.LastWeaponFire);
            Assert.AreSame(damage, details.LastDamage);
        }

        using var legacy = new CombatModule(
            new CommandRegistry(new AllowAll()), players, new FakeRepository());
        await legacy.RecordWeaponFireAsync(new CombatWeaponFireEvent(
            Guid.NewGuid(), Attacker, Now, "de_dust2", "ak47"));
        await legacy.RecordDamageAsync(new CombatDamageEvent(
            Guid.NewGuid(), Victim, Attacker, Now, "de_dust2", "ak47", 1, 1, 0));
    }

    [TestMethod]
    public async Task TopKills_ValidatesPageAndDisplaysStablePositions()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var registry = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository
        {
            TopEntries = [new CombatRankEntry(Attacker, 7, 6, "Leader | name")],
        };
        using var module = new CombatModule(registry, players, repository);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await registry.ExecuteAsync("!anotopkills 0", Attacker)).FailureReason);
        Assert.AreEqual(0, repository.TopCalls);
        var page = await registry.ExecuteAsync("!anotopkills 2", null);
        Assert.IsTrue(page.Success);
        StringAssert.Contains(page.Message!, "6.");
        StringAssert.Contains(page.Message!, "Leader / name");
        Assert.AreEqual(5, repository.LastOffset);
        Assert.AreEqual(5, repository.LastLimit);
    }

    [TestMethod]
    public async Task DeathAndAssistCommands_BoundPagesAndDisposeAllRegistrations()
    {
        var registry = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository
        {
            TopCountEntries = [new CombatCountRankEntry(Victim, 4, 6, "Name |\nline")],
        };
        var module = new CombatModule(registry, new PlayerRegistry(new AnoEventBus()), repository);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await registry.ExecuteAsync("!anotopdeaths 0", null)).FailureReason);
        Assert.AreEqual(0, repository.DeathCalls);
        var deaths = await registry.ExecuteAsync("!anotopdeaths 2", null);
        Assert.IsTrue(deaths.Success);
        StringAssert.Contains(deaths.Message!, "6.");
        StringAssert.Contains(deaths.Message!, "Name / line");
        Assert.AreEqual(5, repository.LastOffset);
        var assists = await registry.ExecuteAsync("!anotopassists", null);
        Assert.IsTrue(assists.Success);
        Assert.AreEqual(1, repository.AssistCalls);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anotopdeaths", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anotopassists", null)).FailureReason);
    }

    [TestMethod]
    public async Task FailedCommandRegistration_RollsBackEarlierCombatCommands()
    {
        var registry = new CommandRegistry(new AllowAll());
        using var occupied = registry.Register(new AnoCore.Abstractions.Modules.ModuleId("other"),
            new CommandDescriptor("anotopassists", "Reserved"),
            _ => ValueTask.FromResult(CommandResult.Ok("reserved")));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new CombatModule(registry, new PlayerRegistry(new AnoEventBus()), new FakeRepository()));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anokda", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anotopkills", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anotopdeaths", null)).FailureReason);
        Assert.AreEqual("reserved",
            (await registry.ExecuteAsync("!anotopassists", null)).Message);
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private class FakeRepository : ICombatRepository
    {
        public CombatDeath? LastRecorded { get; private set; }
        public IReadOnlyList<CombatRankEntry> TopEntries { get; set; } = [];
        public IReadOnlyList<CombatCountRankEntry> TopCountEntries { get; set; } = [];
        public int DeathCalls { get; private set; }
        public int AssistCalls { get; private set; }
        public int TopCalls { get; private set; }
        public int LastOffset { get; private set; }
        public int LastLimit { get; private set; }
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
        {
            LastRecorded = death;
            return ValueTask.CompletedTask;
        }
        public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CombatTotals(1, 2, 3));
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
        {
            TopCalls++;
            LastLimit = limit;
            LastOffset = offset;
            return ValueTask.FromResult(TopEntries);
        }
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit,
            int offset, CancellationToken cancellationToken = default)
        {
            DeathCalls++;
            LastOffset = offset;
            return ValueTask.FromResult(TopCountEntries);
        }
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit,
            int offset, CancellationToken cancellationToken = default)
        {
            AssistCalls++;
            LastOffset = offset;
            return ValueTask.FromResult(TopCountEntries);
        }
    private sealed class DetailRepository : FakeRepository, ICombatDetailRepository
    {
        public CombatWeaponFireEvent? LastWeaponFire { get; private set; }
        public CombatDamageEvent? LastDamage { get; private set; }

        public ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
            CancellationToken cancellationToken = default)
        {
            LastWeaponFire = weaponFire;
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordDamageAsync(CombatDamageEvent damage,
            CancellationToken cancellationToken = default)
        {
            LastDamage = damage;
            return ValueTask.CompletedTask;
        }

        public ValueTask<CombatDetailTotals> ReadDetailsAsync(PlayerId playerId,
            CombatDetailFilter? filter = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CombatDetailTotals(0, 0, 0, 0, 0));

        public ValueTask<IReadOnlyList<CombatHitgroupTotals>> ReadHitgroupsAsync(
            PlayerId playerId, CombatDetailFilter? filter = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatHitgroupTotals>>([]);
    }
    }
}
