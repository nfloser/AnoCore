using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Progression;

public sealed class ChallengeModule : IDisposable
{
    private const int PageSize = 5;
    private readonly ChallengeScheduleSnapshot _schedule;
    private readonly ProgressionDefinitionSnapshot _xp;
    private readonly IPlayerRegistry _players;
    private readonly IChallengeRepository _repository;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Exception>? _reportError;
    private readonly ProgressionEventPublisher? _events;
    private readonly IDisposable _command;
    private readonly ChallengeNotificationService? _notifications;
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public ChallengeModule(ChallengeScheduleSnapshot schedule, ProgressionDefinitionSnapshot xp,
        IPlayerRegistry players, IChallengeRepository repository, IAnoCommandRegistry commands,
        Func<DateTimeOffset>? clock = null, Action<Exception>? reportError = null,
        IPlayerSettingsService? settings = null, IPlayerToggleCatalog? toggles = null, IMessageService? messages = null, IAnoEventBus? events = null)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _xp = xp ?? throw new ArgumentNullException(nameof(xp));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ArgumentNullException.ThrowIfNull(commands);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reportError = reportError;
        _events = events is null ? null : new ProgressionEventPublisher(events, xp, reportError);
        if ((settings is not null || toggles is not null || messages is not null)
            && (settings is null || toggles is null || messages is null))
            throw new ArgumentException("Challenge notifications require settings, toggles and messages together.");
        try
        {
            if (settings is not null && toggles is not null && messages is not null)
                _notifications = new ChallengeNotificationService(players, settings, toggles, messages, reportError);
            _command = commands.Register(new ModuleId("ano.progression.challenges"),
                new CommandDescriptor("anochallenges", "Show your daily, weekly and season challenges.", arguments:
                    [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]), ChallengesAsync);
        }
        catch
        {
            _notifications?.Dispose();
            throw;
        }
    }

    public int CheckpointSeconds => _schedule.CheckpointSeconds;

    public async ValueTask ReconcileOnlineAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _checkpointGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            var catalog = _schedule.ResolveAt(at);
            var ordered = DependencyOrder(catalog);
            foreach (var player in _players.OnlinePlayers.Where(item => item.IsConnected).ToArray())
            {
                foreach (var definition in ordered)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (!Current(player)) break;
                    if (at < definition.StartsAtUtc || at >= definition.EndsAtUtc) continue;
                    try
                    {
                        var result = await _repository.CompleteAsync(player.Id, catalog, definition.Id, at, _xp, linked.Token).ConfigureAwait(false);
                        if (_events is not null)
                            await _events.ChallengeAsync(player, result, linked.Token).ConfigureAwait(false);
                        if (_notifications is not null)
                            await _notifications.NotifyAsync(player, definition.Name, result, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                    catch (Exception exception) { Report(exception); }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally { _checkpointGate.Release(); }
    }

    private async ValueTask<CommandResult> ChallengesAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player) || player is null || !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        var at = _clock();
        var catalog = _schedule.ResolveAt(at);
        var visible = catalog.Challenges.Where(item => at >= item.StartsAtUtc && at < item.EndsAtUtc).ToArray();
        var pages = Math.Max(1, (visible.Length + PageSize - 1) / PageSize);
        var page = context.TryGet<int>("page", out var requested) ? requested : 1;
        if (page < 1 || page > pages) return CommandResult.Fail(CommandFailureReason.InvalidInput, $"Choose page 1-{pages}.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var lines = new List<string> { $"Challenges {page}/{pages}" };
        if (visible.Length == 0) lines.Add("No active challenges.");
        foreach (var definition in visible.Skip((page - 1) * PageSize).Take(PageSize))
        {
            var status = await _repository.ReadAsync(player.Id, catalog, definition.Id, at, linked.Token).ConfigureAwait(false);
            if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
            var name = new string(definition.Name.Where(character => !char.IsControl(character)
                && character is not '{' and not '}' and not '|').Take(48).ToArray());
            lines.Add($"{name}: {status.State}, {status.Progress}/{status.Target}, base reward {definition.RewardXp} XP, ends {definition.EndsAtUtc:yyyy-MM-dd HH:mm} UTC");
        }
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        return CommandResult.Ok(string.Join(" | ", lines));
    }

    private static IReadOnlyList<ChallengeDefinition> DependencyOrder(ChallengeCatalogSnapshot catalog)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ChallengeDefinition>();
        foreach (var definition in catalog.Challenges) Visit(definition);
        return ordered;

        void Visit(ChallengeDefinition definition)
        {
            if (!visited.Add(definition.Id)) return;
            foreach (var id in definition.PrerequisiteIds) Visit(catalog.Get(id));
            ordered.Add(definition);
        }
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not interrupt other challenges. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _notifications?.Dispose();
        _command.Dispose();
    }
}
