using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class CombatModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly ICombatRepository _repository;
    private readonly ICombatDetailRepository? _detailRepository;
    private readonly RankTransitionMonitor? _transitionMonitor;
    private readonly IDisposable _command;
    private readonly IDisposable _topCommand;
    private readonly IDisposable _deathCommand;
    private readonly IDisposable _assistCommand;
    private readonly IDisposable? _detailCommand;
    private readonly IDisposable? _hitgroupCommand;
    private int _disposed;

    public CombatModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        ICombatRepository repository, RankTransitionMonitor? transitionMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _detailRepository = repository as ICombatDetailRepository;
        _transitionMonitor = transitionMonitor;
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
                throw;
            }
        }
        catch
        {
            _command.Dispose();
            throw;
        }

        if (_detailRepository is not null)
        {
            try
            {
                _detailCommand = commands.Register(new ModuleId("ano.stats"),
                    new CommandDescriptor("anodetailstats",
                        "Show your shot, hit and damage statistics.", arguments:
                        [
                            new("map", CommandArgumentKind.String, "Optional map name.", required: false),
                            new("weapon", CommandArgumentKind.String, "Optional weapon name.", required: false),
                        ]),
                    OwnDetailStatsAsync);
                try
                {
                    _hitgroupCommand = commands.Register(new ModuleId("ano.stats"),
                        new CommandDescriptor("anohitgroups",
                            "Show your hitgroup damage breakdown.", arguments:
                            [
                                new("map", CommandArgumentKind.String, "Optional map name.", required: false),
                                new("weapon", CommandArgumentKind.String, "Optional weapon name.", required: false),
                            ]),
                        OwnHitgroupsAsync);
                }
                catch
                {
                    _detailCommand?.Dispose();
                    throw;
                }
            }
            catch
            {
                _assistCommand.Dispose();
                _deathCommand.Dispose();
                _topCommand.Dispose();
                _command.Dispose();
                throw;
            }
        }
    }

    public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _transitionMonitor is null
            ? _repository.RecordAsync(death, cancellationToken)
            : _transitionMonitor.RecordAsync(death, cancellationToken);
    }

    public ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(weaponFire);
        return _detailRepository is null
            ? ValueTask.CompletedTask
            : _detailRepository.RecordWeaponFireAsync(weaponFire, cancellationToken);
    }

    public ValueTask RecordDamageAsync(CombatDamageEvent damage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(damage);
        return _detailRepository is null
            ? ValueTask.CompletedTask
            : _detailRepository.RecordDamageAsync(damage, cancellationToken);
    }

    private async ValueTask<CommandResult> OwnDetailStatsAsync(CommandContext context)
    {
        if (_detailRepository is null)
            return CommandResult.Fail(CommandFailureReason.NotFound,
                "Combat detail statistics are unavailable.");
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is null || !player.IsConnected)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");

        if (!TryDetailFilter(context, out var filter, out var failure))
            return failure!;

        var sessionId = player.SessionId;
        var totals = await _detailRepository.ReadDetailsAsync(
            context.Caller, filter, context.CancellationToken).ConfigureAwait(false);
        if (!IsCurrentSession(context.Caller, sessionId))
            return SessionChanged();
        return CommandResult.Ok(
            $"[ANO] Detail stats: {totals.Shots} shot(s), {totals.Hits} hit(s), "
            + $"{totals.DamageHealth} health damage, {totals.DamageArmor} armor damage, "
            + $"{totals.HeadHits} head hit(s).");
    }

    private async ValueTask<CommandResult> OwnHitgroupsAsync(CommandContext context)
    {
        if (_detailRepository is null)
            return CommandResult.Fail(CommandFailureReason.NotFound,
                "Combat detail statistics are unavailable.");
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is null || !player.IsConnected)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");

        if (!TryDetailFilter(context, out var filter, out var failure))
            return failure!;

        var sessionId = player.SessionId;
        var entries = await _detailRepository.ReadHitgroupsAsync(
            context.Caller, filter, context.CancellationToken).ConfigureAwait(false);
        if (!IsCurrentSession(context.Caller, sessionId))
            return SessionChanged();
        if (entries.Count == 0)
            return CommandResult.Ok("[ANO] No hitgroup statistics match this filter.");
        return CommandResult.Ok("[ANO] " + string.Join(" | ", entries.Take(16).Select(entry =>
            $"HG{entry.Hitgroup}: {entry.Hits} hit(s), {entry.DamageHealth} health damage")));
    }

    private bool IsCurrentSession(PlayerId playerId, PlayerSessionId sessionId)
        => Volatile.Read(ref _disposed) == 0
            && _players.TryGet(playerId, out var current)
            && current is { IsConnected: true }
            && current.SessionId == sessionId;

    private static CommandResult SessionChanged()
        => CommandResult.Fail(CommandFailureReason.InvalidInput,
            "Player session changed before the combat detail query completed.");

    private static bool TryDetailFilter(CommandContext context,
        out CombatDetailFilter filter, out CommandResult? failure)
    {
        var map = context.ParsedArguments.TryGetValue("map", out var mapValue)
            ? (string?)mapValue : null;
        var weapon = context.ParsedArguments.TryGetValue("weapon", out var weaponValue)
            ? (string?)weaponValue : null;
        try
        {
            filter = new CombatDetailFilter(map, weapon);
            failure = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            filter = new CombatDetailFilter();
            failure = CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
            return false;
        }
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
        _transitionMonitor?.Dispose();
        _hitgroupCommand?.Dispose();
        _detailCommand?.Dispose();
        _assistCommand.Dispose();
        _deathCommand.Dispose();
        _topCommand.Dispose();
        _command.Dispose();
    }
}
