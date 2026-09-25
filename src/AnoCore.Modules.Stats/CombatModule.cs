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
        _topCommand = commands.Register(new ModuleId("ano.stats"),
            new CommandDescriptor("anotopkills", "Show the kill leaderboard.", arguments:
            [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]),
            TopKillsAsync);
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

    private static string Display(CombatRankEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PlayerId.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{entry.DisplayName.Replace('\\r', ' ').Replace('\\n', ' ').Replace('|', '/')} "
                + $"({entry.PlayerId.SteamId64})";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _topCommand.Dispose();
        _command.Dispose();
    }
}
