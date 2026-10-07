using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed class AchievementModule : IDisposable
{
    private const int PageSize = 5;
    private readonly AchievementCatalogSnapshot _catalog;
    private readonly IPlayerRegistry _players;
    private readonly IGameplayStatRepository _statistics;
    private readonly IAchievementRepository _achievements;
    private readonly ProgressionGrantService _grants;
    private readonly Action<Exception>? _reportError;
    private readonly ProgressionEventPublisher? _events;
    private readonly AchievementNotificationService? _notifications;
    private readonly List<IDisposable> _registrations = [];
    private readonly Dictionary<PlayerId, (PlayerSessionId Session, GameplayStatTotal[] Totals)> _checked = [];
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public AchievementModule(AchievementCatalogSnapshot catalog, IPlayerRegistry players,
        IGameplayStatRepository statistics, IAchievementRepository achievements,
        IProgressionGrantRepository grants, IAnoCommandRegistry commands, Action<Exception>? reportError = null,
        IPlayerSettingsService? settings = null, IPlayerToggleCatalog? toggles = null, IMessageService? messages = null, IAnoEventBus? events = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Xp is null || catalog.Achievements is null)
            throw new ArgumentException("An achievement catalog requires XP and achievement definitions.", nameof(catalog));
        var validationCopy = new AchievementConfiguration
        {
            CheckpointSeconds = catalog.CheckpointSeconds,
            Levels = catalog.Xp.Levels.ToList(),
            Boosts = catalog.Xp.Boosts.ToList(),
            Achievements = catalog.Achievements.Select(entry => entry is null || entry.Definition is null
                ? throw new ArgumentException("Catalog entries cannot be null.", nameof(catalog))
                : new AchievementCatalogEntry(entry.Definition.Id, entry.Definition.Version, entry.Name,
                    entry.Definition.Statistic, entry.Definition.Tiers.ToList())
                { Prerequisites = entry.Definition.Prerequisites.ToList() }).ToList(),
        };
        _catalog = validationCopy.Snapshot();
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _grants = new ProgressionGrantService(grants, _catalog.Xp);
        _reportError = reportError;
        _events = events is null ? null : new ProgressionEventPublisher(events, _catalog.Xp, reportError);
        ArgumentNullException.ThrowIfNull(commands);
        if ((settings is not null || toggles is not null || messages is not null)
            && (settings is null || toggles is null || messages is null))
            throw new ArgumentException("Achievement notifications require settings, toggles and messages together.");
        try
        {
            if (settings is not null && toggles is not null && messages is not null)
                _notifications = new AchievementNotificationService(players, settings, toggles, messages, reportError);
            var owner = new ModuleId("ano.progression");
            _registrations.Add(commands.Register(owner,
                new CommandDescriptor("anolevel", "Show your independent lifetime XP and level."), LevelAsync));
            _registrations.Add(commands.Register(owner,
                new CommandDescriptor("anoachievements", "Show permanent achievement progress.", arguments:
                    [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]), AchievementsAsync));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int CheckpointSeconds => _catalog.CheckpointSeconds;

    public async ValueTask ReconcileOnlineAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        if (!await _checkpointGate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            var players = _players.OnlinePlayers.Where(player => player.IsConnected).ToArray();
            var online = players.Select(player => player.Id).ToHashSet();
            foreach (var id in _checked.Keys.Where(id => !online.Contains(id)).ToArray()) _checked.Remove(id);
            foreach (var player in players)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var totals = (await _statistics.ReadAsync(player.Id, cancellationToken: token).ConfigureAwait(false))
                        .OrderBy(total => total.Kind).ToArray();
                    if (!Current(player)) continue;
                    if (_checked.TryGetValue(player.Id, out var previous)
                        && previous.Session == player.SessionId && previous.Totals.SequenceEqual(totals)) continue;
                    foreach (var entry in _catalog.Achievements)
                    {
                        var unlocked = await _achievements.UnlockAsync(player.Id, entry.Definition, totals, at, _catalog.Xp, token).ConfigureAwait(false);
                        if (_events is not null)
                            foreach (var unlock in unlocked.OrderBy(item => item.Grant.AccountRevisionAfter))
                                await _events.AchievementAsync(player, unlock, token).ConfigureAwait(false);
                        if (_notifications is not null)
                            await _notifications.NotifyAsync(player, entry.Name, unlocked, token).ConfigureAwait(false);
                    }
                    if (Current(player)) _checked[player.Id] = (player.SessionId, totals);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Report(exception);
                }
            }
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    private async ValueTask<CommandResult> LevelAsync(CommandContext context)
    {
        var player = Caller(context);
        if (player is null) return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var state = await _grants.ReadLifetimeAsync(player.Id, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        return CommandResult.Ok($"Level {state.Level.Level} | Lifetime XP: {state.LifetimeXp}. Independent from rank points.");
    }

    private async ValueTask<CommandResult> AchievementsAsync(CommandContext context)
    {
        var player = Caller(context);
        if (player is null) return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        var page = context.TryGet<int>("page", out var requested) ? requested : 1;
        var pages = (_catalog.Achievements.Count + PageSize - 1) / PageSize;
        if (page < 1 || page > pages) return CommandResult.Fail(CommandFailureReason.InvalidInput, $"Choose page 1-{pages}.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var token = linked.Token;
        var totals = await _statistics.ReadAsync(player.Id, cancellationToken: token).ConfigureAwait(false);
        var lines = new List<string> { $"Achievements {page}/{pages}" };
        foreach (var entry in _catalog.Achievements.Skip((page - 1) * PageSize).Take(PageSize))
        {
            var awarded = await _achievements.ReadAwardedTierAsync(player.Id, entry.Definition.Id, token).ConfigureAwait(false);
            var status = entry.Definition.Evaluate(totals, awarded);
            var target = status.NextTarget is { } next ? $"{status.Count}/{next}" : "complete";
            var missing = new List<string>();
            if (awarded < entry.Definition.Tiers.Count)
            {
                foreach (var requirement in entry.Definition.Prerequisites)
                {
                    var parentTier = await _achievements.ReadAwardedTierAsync(player.Id, requirement.AchievementId, token).ConfigureAwait(false);
                    if (parentTier < requirement.Tier) missing.Add($"{requirement.AchievementId} tier {requirement.Tier}");
                }
            }
            var locked = missing.Count > 0 ? $", locked: {string.Join(", ", missing)}" : "";
            lines.Add($"{entry.Name}: awarded {awarded}/{entry.Definition.Tiers.Count}, progress {target}{locked}");
        }
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        return CommandResult.Ok(string.Join(" | ", lines));
    }

    private PlayerSnapshot? Caller(CommandContext context)
        => context.Caller is not null && _players.TryGet(context.Caller, out var player)
            && player is not null && Current(player) ? player : null;

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _notifications?.Dispose();
        foreach (var registration in _registrations) registration.Dispose();
    }

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not interrupt other players. */ }
    }
}
