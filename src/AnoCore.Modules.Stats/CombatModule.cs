using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class CombatModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly ICombatRepository _repository;
    private readonly IDisposable _command;
    private readonly IDisposable _topCommand;
    private readonly IDisposable _deathCommand;
    private readonly IDisposable _assistCommand;
    private int _disposed;

    public CombatModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        ICombatRepository repository)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _command = commands.Register(new ModuleId("ano.stats"),
            new CommandDescriptor("anokda", "Show your kill, death and assist totals."),
            context => OwnStatsAsync(context.Caller, context.CancellationToken));
        try
        {
            _topCommand = commands.Register(new ModuleId("ano.stats"),
                new CommandDescriptor("anotopkills", "Show the kill leaderboard.", arguments:
                [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]),
                TopKillsAsync);
            try
            {
                _deathCommand = commands.Register(new ModuleId("ano.stats"),
                    new CommandDescriptor("anotopdeaths", "Show the death leaderboard.", arguments:
                    [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]),
                    context => TopCountsAsync(context, _repository.GetTopDeathsAsync, "death"));
                try
                {
                    _assistCommand = commands.Register(new ModuleId("ano.stats"),
                        new CommandDescriptor("anotopassists", "Show the assist leaderboard.", arguments:
                        [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]),
                        context => TopCountsAsync(context, _repository.GetTopAssistsAsync, "assist"));
                }
                catch
                {
                    _deathCommand.Dispose();
                    throw;
                }
            }
            catch
            {
                _topCommand.Dispose();
        _deathCommand.Dispose();
        _assistCommand.Dispose();
                throw;
            }
        }
        catch
        {
            _command.Dispose();
            throw;
        }
    }

    public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _repository.RecordAsync(death, cancellationToken);
    }

    private async ValueTask<CommandResult> OwnStatsAsync(PlayerId? caller,
        CancellationToken cancellationToken)
    {
        if (caller is null || !_players.TryGet(caller, out var player)
            || player is null || !player.IsConnected)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");
        var totals = await _repository.ReadAsync(caller, cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok(
            $"[ANO] {totals.Kills} kill(s), {totals.Deaths} death(s), {totals.Assists} assist(s).");
    }

    private async ValueTask<CommandResult> TopKillsAsync(CommandContext context)
    {
        var page = context.ParsedArguments.TryGetValue("page", out var provided)
            ? (int)provided! : 1;
        if (page is < 1 or > 1000)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Page must be between 1 and 1000.");
        const int pageSize = 5;
        var entries = await _repository.GetTopKillsAsync(pageSize, (page - 1) * pageSize,
            context.CancellationToken).ConfigureAwait(false);
        if (entries.Count == 0) return CommandResult.Ok("No kill entries on this page.");
        return CommandResult.Ok(string.Join(" | ", entries.Select(entry =>
            $"{entry.Position}. {Display(entry)}: {entry.Kills} kill(s)")));
    }

    private async ValueTask<CommandResult> TopCountsAsync(
        CommandContext context,
        Func<int, int, CancellationToken, ValueTask<IReadOnlyList<CombatCountRankEntry>>> query,
        string label)
    {
        var page = context.ParsedArguments.TryGetValue("page", out var provided)
            ? (int)provided! : 1;
        if (page is < 1 or > 1000)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Page must be between 1 and 1000.");
        const int pageSize = 5;
        var entries = await query(pageSize, (page - 1) * pageSize, context.CancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0) return CommandResult.Ok($"No {label} entries on this page.");
        return CommandResult.Ok(string.Join(" | ", entries.Select(entry =>
            $"{entry.Position}. {Display(entry.PlayerId, entry.DisplayName)}: {entry.Count} {label}(s)")));
    }

    private static string Display(CombatRankEntry entry)
        => Display(entry.PlayerId, entry.DisplayName);

    private static string Display(PlayerId id, string? displayName)
        => string.IsNullOrWhiteSpace(displayName)
            ? id.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{displayName.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/')} "
                + $"({id.SteamId64})";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _topCommand.Dispose();
        _command.Dispose();
    }
}
