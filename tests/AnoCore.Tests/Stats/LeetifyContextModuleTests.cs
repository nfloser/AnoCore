using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class LeetifyContextModuleTests
{
    private static readonly PlayerId Alice = new(76561198000029901);
    private static readonly PlayerId Bob = new(76561198000029902);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Command_PresentsExternalDataSeparatelyWithAttributionAndLink()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Alice, "Alice", PlayerTeam.CounterTerrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(
            Bob, "Bob", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var provider = new Provider(new LeetifyLookupResult(
            LeetifyLookupStatus.Available,
            new LeetifyProfileContext(
                Bob,
                "Leetify Bob",
                [
                    new("Aim", "95.25"),
                    new("Positioning", "61"),
                    new("Utility", "42.500"),
                ],
                new Uri($"https://leetify.com/app/profile/{Bob.SteamId64}"))));
        using var module = new LeetifyContextModule(commands, players, provider);

        var result = await commands.ExecuteAsync("!anoleetify Bob", Alice);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "Data Provided by Leetify");
        StringAssert.Contains(result.Message!, "Aim=95.25");
        StringAssert.Contains(result.Message!, "Positioning=61");
        StringAssert.Contains(result.Message!, "Utility=42.500");
        StringAssert.Contains(result.Message!, "View on Leetify:");
        StringAssert.Contains(result.Message!, Bob.SteamId64.ToString());
        Assert.AreEqual(Bob, provider.Requested.Single());
    }

    [TestMethod]
    public async Task Command_UnavailableProviderDoesNotAffectInternalAnoRatingCommand()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Alice, "Alice", PlayerTeam.CounterTerrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        using var external = new LeetifyContextModule(
            commands, players,
            new Provider(new LeetifyLookupResult(LeetifyLookupStatus.RateLimited)));

        var externalResult = await commands.ExecuteAsync("!anoleetify Alice", Alice);

        Assert.AreEqual(CommandFailureReason.NotFound, externalResult.FailureReason);
        StringAssert.Contains(externalResult.Message!, "rate limited");
        Assert.AreEqual(
            CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorating", Alice)).FailureReason);
    }

    [TestMethod]
    public async Task Command_RejectsStaleTargetResultAfterReconnect()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            Alice, "Alice", PlayerTeam.CounterTerrorist, true, Now));
        var bob = await players.ConnectAsync(new PlayerConnection(
            Bob, "Bob", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var provider = new Provider(new LeetifyLookupResult(LeetifyLookupStatus.NotFound))
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(Bob, bob.SessionId, Now.AddSeconds(1));
                await players.ConnectAsync(new PlayerConnection(
                    Bob, "Replacement", PlayerTeam.Terrorist, true, Now.AddSeconds(2)));
            },
        };
        using var module = new LeetifyContextModule(commands, players, provider);

        var result = await commands.ExecuteAsync("!anoleetify Bob", Alice);

        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        StringAssert.Contains(result.Message!, "session changed");
    }

    [TestMethod]
    public async Task Command_RequiresOneConnectedTargetAndDisposesRegistration()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Alice, "Same", PlayerTeam.CounterTerrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(
            Bob, "SameName", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var module = new LeetifyContextModule(
            commands, players,
            new Provider(new LeetifyLookupResult(LeetifyLookupStatus.NotFound)));

        Assert.AreEqual(
            CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anoleetify Same", Alice)).FailureReason);
        Assert.AreEqual(
            CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anoleetify Missing", Alice)).FailureReason);
        Assert.IsTrue(
            (await commands.ExecuteAsync($"!anoleetify {Bob.SteamId64}", Alice)).FailureReason
            == CommandFailureReason.NotFound);

        module.Dispose();
        Assert.AreEqual(
            CommandFailureReason.NotFound,
            (await commands.ExecuteAsync($"!anoleetify {Bob.SteamId64}", Alice)).FailureReason);
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId id,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class Provider(LeetifyLookupResult result) : ILeetifyProfileProvider
    {
        public List<PlayerId> Requested { get; } = [];
        public Func<ValueTask>? BeforeRead { get; init; }

        public async ValueTask<LeetifyLookupResult> ReadAsync(
            PlayerId player,
            CancellationToken cancellationToken = default)
        {
            Requested.Add(player);
            if (BeforeRead is not null)
                await BeforeRead();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
