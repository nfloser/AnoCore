using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed class SeasonModule : IDisposable
{
    public static readonly PermissionId ClosePermission = new("ano.progression.season.close");
    private const int PageSize = 5;
    private readonly SeasonConfigurationSnapshot _configuration;
    private readonly ProgressionDefinitionSnapshot _xp;
    private readonly IPlayerRegistry _players;
    private readonly ISeasonRepository _seasons;
    private readonly ISeasonProgressionRepository _progression;
    private readonly ISeasonRewardRepository _rewards;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Exception>? _reportError;
    private readonly List<IDisposable> _commands = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public static async ValueTask<SeasonModule> CreateAsync(SeasonConfigurationSnapshot configuration,
        ProgressionDefinitionSnapshot xp, IPlayerRegistry players, ISeasonRepository seasons,
        ISeasonProgressionRepository progression, ISeasonRewardRepository rewards, IAnoCommandRegistry commands,
        DateTimeOffset acceptedAt, Func<DateTimeOffset>? clock = null, Action<Exception>? reportError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Catalog);
        ArgumentNullException.ThrowIfNull(xp);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(rewards);
        ArgumentNullException.ThrowIfNull(commands);
        // Revalidate the public snapshot before any durable writes or command registration.
        var validated = new SeasonConfiguration
        {
            CheckpointSeconds = configuration.CheckpointSeconds,
            BatchSize = configuration.BatchSize,
            Seasons = configuration.Catalog.Seasons.ToList(),
        }.Snapshot();
        foreach (var definition in validated.Catalog.Seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await seasons.AcceptAsync(definition, acceptedAt, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(validated, xp, players, seasons, progression, rewards, commands, clock, reportError);
    }

    private SeasonModule(SeasonConfigurationSnapshot configuration, ProgressionDefinitionSnapshot xp,
        IPlayerRegistry players, ISeasonRepository seasons, ISeasonProgressionRepository progression,
        ISeasonRewardRepository rewards, IAnoCommandRegistry commands, Func<DateTimeOffset>? clock,
        Action<Exception>? reportError)
    {
        _configuration = configuration;
        _xp = xp;
        _players = players;
        _seasons = seasons;
        _progression = progression;
        _rewards = rewards;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reportError = reportError;
        var owner = new ModuleId("ano.progression.seasons");
        try
        {
            _commands.Add(commands.Register(owner, new("anoseason", "Show your current or historical season XP.",
                arguments: [new("season", CommandArgumentKind.String, "Season ID (default current).", required: false)]), OwnAsync));
            _commands.Add(commands.Register(owner, new("anoseasons", "List accepted season history and windows.",
                arguments: [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]), ListAsync));
            _commands.Add(commands.Register(owner, new("anoseasontop", "Show an independent season leaderboard.", arguments:
                [new("season", CommandArgumentKind.String, "Season ID (default current).", required: false),
                new("page", CommandArgumentKind.Int32, "Page number.", required: false)]), TopAsync));
            _commands.Add(commands.Register(owner, new("anoseasontopcurrent", "Show the current season leaderboard.", arguments:
                [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]), TopAsync));
            _commands.Add(commands.Register(owner, new("anocloseseason", "Freeze an ended season after reward backlogs are drained.",
                ClosePermission, arguments: [new("season", CommandArgumentKind.String, "Season ID.")]), CloseAsync));
        }
        catch { Dispose(); throw; }
    }

    public int CheckpointSeconds => _configuration.CheckpointSeconds;

    public async ValueTask ReconcileAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _gate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            var seasons = await _seasons.ListEffectiveAsync(linked.Token).ConfigureAwait(false);
            foreach (var season in seasons.Where(item => item.ClosedAtUtc is null && at >= item.Definition.StartsAtUtc))
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    await _rewards.ReconcileAsync(season, _configuration.BatchSize, at, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception exception) { Report(exception); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        finally { _gate.Release(); }
    }

    private async ValueTask<CommandResult> OwnAsync(CommandContext context)
    {
        var player = Caller(context);
        if (player is null) return ConnectedRequired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var season = await ResolveAsync(context, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        if (season is null) return Missing(context);
        var state = await _progression.ReadAsync(player.Id, season.Definition.Id, season.Definition.Version, linked.Token)
            .ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        return CommandResult.Ok($"{Label(season)} | Level {_xp.LevelFor(state.SeasonXp).Level} | {state.SeasonXp} XP. Independent from lifetime and rank points.");
    }

    private async ValueTask<CommandResult> ListAsync(CommandContext context)
    {
        var player = Caller(context);
        if (player is null) return ConnectedRequired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var seasons = (await _seasons.ListEffectiveAsync(linked.Token).ConfigureAwait(false))
            .OrderByDescending(item => item.Definition.StartsAtUtc).ToArray();
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        var pages = Math.Max(1, (seasons.Length + PageSize - 1) / PageSize);
        var page = Page(context);
        if (page < 1 || page > pages) return CommandResult.Fail(CommandFailureReason.InvalidInput, $"Choose page 1-{pages}.");
        var lines = new List<string> { $"Seasons {page}/{pages}" };
        foreach (var season in seasons.Skip((page - 1) * PageSize).Take(PageSize))
            lines.Add($"{Label(season)}: {season.Definition.StartsAtUtc:yyyy-MM-dd HH:mm} to {season.Definition.EndsAtUtc:yyyy-MM-dd HH:mm} UTC");
        if (seasons.Length == 0) lines.Add("No accepted seasons.");
        return CommandResult.Ok(string.Join(" | ", lines));
    }

    private async ValueTask<CommandResult> TopAsync(CommandContext context)
    {
        var player = Caller(context);
        if (player is null) return ConnectedRequired();
        var page = Page(context);
        if (page is < 1 or > 20001) return CommandResult.Fail(CommandFailureReason.InvalidInput, "Choose page 1-20001.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var season = await ResolveAsync(context, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        if (season is null) return Missing(context);
        var entries = await _rewards.ReadTopAsync(season.Definition.Id, season.Definition.Version,
            (page - 1) * PageSize, PageSize, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        var lines = new List<string> { $"{Label(season)} leaderboard, page {page}" };
        foreach (var entry in entries)
            lines.Add($"#{entry.Placement}: {entry.PlayerId.SteamId64}, Level {_xp.LevelFor(entry.SeasonXp).Level}, {entry.SeasonXp} XP");
        if (entries.Count == 0) lines.Add("No entries on this page.");
        return CommandResult.Ok(string.Join(" | ", lines));
    }

    private async ValueTask<CommandResult> CloseAsync(CommandContext context)
    {
        if (Volatile.Read(ref _disposed) != 0) return CommandResult.Fail(CommandFailureReason.Forbidden);
        var player = context.Caller is null ? null : Caller(context);
        if (context.Caller is not null && player is null) return ConnectedRequired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var season = await ResolveAsync(context, linked.Token).ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) != 0 || player is not null && !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden);
        if (season is null) return Missing(context);
        var now = _clock();
        if (now < season.Definition.EndsAtUtc)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "The season has not ended yet.");
        await _seasons.CloseAsync(season.Definition.Id, season.Definition.Version, now, linked.Token).ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) != 0 || player is not null && !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden);
        return CommandResult.Ok($"Season {season.Definition.Id} is frozen; later pending rewards cannot enter this season.");
    }

    private async ValueTask<PersistedSeason?> ResolveAsync(CommandContext context, CancellationToken token)
    {
        var seasons = await _seasons.ListEffectiveAsync(token).ConfigureAwait(false);
        if (context.TryGet<string>("season", out var id))
            return seasons.FirstOrDefault(item => string.Equals(item.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
        var at = _clock();
        return seasons.FirstOrDefault(item => item.Definition.StartsAtUtc <= at && at < item.Definition.EndsAtUtc);
    }

    private static int Page(CommandContext context) => context.TryGet<int>("page", out var page) ? page : 1;
    private static CommandResult Missing(CommandContext context) => context.TryGet<string>("season", out _)
        ? CommandResult.Fail(CommandFailureReason.InvalidInput, "Unknown season ID.") : CommandResult.Ok("No active season.");
    private static CommandResult ConnectedRequired() => CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
    private static string Label(PersistedSeason season)
    {
        var name = new string(season.Definition.Name.Where(character => !char.IsControl(character)
            && character is not '{' and not '}' and not '|').Take(48).ToArray());
        return $"{season.Definition.Id}: {name}{(season.ClosedAtUtc is null ? string.Empty : " (frozen)")}";
    }
    private PlayerSnapshot? Caller(CommandContext context) => context.Caller is not null
        && _players.TryGet(context.Caller, out var player) && player is not null && Current(player) ? player : null;
    private bool Current(PlayerSnapshot player) => Volatile.Read(ref _disposed) == 0
        && _players.TryGet(player.Id, out var current) && current is { IsConnected: true } && current.SessionId == player.SessionId;
    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not interrupt other seasons. */ }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        foreach (var command in _commands) command.Dispose();
    }
}
