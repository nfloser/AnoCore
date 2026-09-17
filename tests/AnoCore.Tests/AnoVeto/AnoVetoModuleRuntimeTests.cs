using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoModuleRuntimeTests
{
    private static readonly PlayerId Manager = new(76561198000000401);
    private static readonly PlayerId PlayerA = new(76561198000000402);

    [TestMethod]
    public async Task CreateAsync_DisabledConfigurationDoesNotRegisterCommand()
    {
        var path = CreateTempDirectory();
        try
        {
            var config = new JsonConfigStore(path);
            await config.SaveAsync(
                "anoveto",
                new AnoVetoConfiguration { Enabled = false },
                AnoVetoConfiguration.Validate);
            var commands = new CommandRegistry(new AllowManagerPermissions());

            using var module = await AnoVetoModuleRuntime.CreateAsync(
                config,
                commands,
                new TestCustomHudService(),
                new StubPlayerRegistry(),
                new VoteService(new AllowManagerPermissions()),
                new RecordingMapChanger());

            Assert.IsNull(module);
            Assert.IsFalse(commands.GetCommands().Any(command => command.Name == "anoveto"));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [TestMethod]
    public async Task CreateAsync_LoadsConfiguredMapsAndRegistersWorkingCommandAndHud()
    {
        var path = CreateTempDirectory();
        try
        {
            var config = new JsonConfigStore(path);
            await config.SaveAsync(
                "anoveto",
                new AnoVetoConfiguration(),
                AnoVetoConfiguration.Validate);
            await config.SaveAsync(
                "maps",
                new MapCatalogConfiguration(Enumerable.Range(1, 8)
                    .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}"))
                    .ToArray()));
            var permissions = new AllowManagerPermissions();
            var commands = new CommandRegistry(permissions);
            var hud = new TestCustomHudService();
            var players = new StubPlayerRegistry(
            [
                Snapshot(Manager, "Manager"),
                Snapshot(PlayerA, "Player A"),
            ]);

            using var module = await AnoVetoModuleRuntime.CreateAsync(
                config,
                commands,
                hud,
                players,
                new VoteService(permissions),
                new RecordingMapChanger(),
                random: new StableRandomSource());

            Assert.IsNotNull(module);
            var result = await commands.ExecuteAsync("!anoveto create", Manager);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(module.Coordinator.TryGetStatus(out var maps));
            Assert.HasCount(8, maps);
            Assert.IsNotNull(hud.Definition(AnoVetoHudController.HudId));
            Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "anoveto-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static PlayerSnapshot Snapshot(PlayerId id, string name)
    {
        var now = new DateTimeOffset(2026, 9, 17, 13, 30, 0, TimeSpan.Zero);
        return new PlayerSnapshot(
            id,
            PlayerSessionId.New(),
            name,
            isConnected: true,
            isAlive: true,
            PlayerTeam.CounterTerrorist,
            now,
            now);
    }

    private sealed class AllowManagerPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
    }

    private sealed class StubPlayerRegistry(IReadOnlyCollection<PlayerSnapshot>? players = null) : IPlayerRegistry
    {
        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers { get; } = players ?? [];

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
        {
            player = OnlinePlayers.SingleOrDefault(candidate => candidate.Id == id);
            return player is not null;
        }

        public ValueTask<PlayerSnapshot> ConnectAsync(PlayerConnection connection, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> UpdateAsync(PlayerStateUpdate update, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> DisconnectAsync(
            PlayerId id,
            PlayerSessionId sessionId,
            DateTimeOffset disconnectedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingMapChanger : IMapChanger
    {
        public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class StableRandomSource : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count)
            => source.Take(count).ToArray();
    }
}
