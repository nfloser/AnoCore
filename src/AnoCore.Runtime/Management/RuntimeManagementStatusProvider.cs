using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Modules;

namespace AnoCore.Runtime.Management;

public sealed class RuntimeManagementStatusProvider : IManagementStatusProvider
{
    private readonly IDatabase _database;
    private readonly IPlayerRegistry _players;
    private readonly ModuleHost _modules;
    private readonly TimeProvider _time;

    public RuntimeManagementStatusProvider(
        IDatabase database,
        IPlayerRegistry players,
        ModuleHost modules,
        TimeProvider? timeProvider = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<ManagementHealthSnapshot> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var ready = false;
        try
        {
            ready = await _database.PingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ready = false;
        }

        return new ManagementHealthSnapshot(
            ready,
            ready ? "ready" : "database_unavailable",
            _time.GetUtcNow());
    }

    public ValueTask<ManagementServerStatus> GetServerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connected = _players.OnlinePlayers.Count(player => player.IsConnected);
        return ValueTask.FromResult(new ManagementServerStatus(
            ManagementApiVersion.Current,
            AnoCoreApi.CurrentLevel,
            connected,
            _time.GetUtcNow()));
    }

    public ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var players = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64)
            .Select(player => new ManagementPlayerStatus(
                player.Id,
                SanitizeName(player.Name),
                true,
                player.Team.ToString()))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ManagementPlayerStatus>>(players);
    }

    public ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modules = _modules.Modules
            .OrderBy(module => module.Descriptor.Id.Value, StringComparer.Ordinal)
            .Select(module => new ManagementModuleStatus(
                module.Descriptor.Id,
                module.State.ToString()))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ManagementModuleStatus>>(modules);
    }

    private static string SanitizeName(string value)
    {
        var safe = new string(value
            .Select(character => char.IsControl(character) ? ' ' : character)
            .Take(64)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Unknown" : safe;
    }
}
