using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed class GameplayXpModule : IDisposable
{
    private readonly GameplayXpPolicy _policy;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ProgressionDefinitionSnapshot _definitions;
    private readonly IPlayerRegistry _players;
    private readonly IGameplayXpRepository _repository;
    private readonly ProgressionGrantService _grants;
    private readonly Action<Exception>? _reportError;
    private readonly ProgressionEventPublisher? _events;
    private readonly IDisposable _command;
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public GameplayXpModule(GameplayXpPolicy policy, ProgressionDefinitionSnapshot definitions,
        IPlayerRegistry players, IGameplayXpRepository repository, IProgressionGrantRepository grants,
        IAnoCommandRegistry commands, Action<Exception>? reportError = null, IAnoEventBus? events = null, Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _grants = new ProgressionGrantService(grants, definitions);
        ArgumentNullException.ThrowIfNull(commands);
        _reportError = reportError;
        _events = events is null ? null : new ProgressionEventPublisher(events, definitions, reportError);
        _command = commands.Register(new ModuleId("ano.progression.gameplay-xp"),
            new CommandDescriptor("anoxp", "Show your independent lifetime XP and level."), XpAsync);
    }

    public int CheckpointSeconds => _policy.CheckpointSeconds;

    public async ValueTask ReconcileOnlineAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _checkpointGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            foreach (var player in _players.OnlinePlayers.Where(item => item.IsConnected).ToArray())
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!Current(player)) continue;
                try
                {
                    var committed = await _repository.ReconcileAsync(player.Id, _policy, _definitions, at, linked.Token).ConfigureAwait(false);
                    if (_events is not null)
                        foreach (var grant in committed.OrderBy(item => item.AccountRevisionAfter))
                            await _events.GrantAsync(player, grant, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception exception) { Report(exception); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally { _checkpointGate.Release(); }
    }

    private async ValueTask<CommandResult> XpAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player) || player is null || !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.Token);
        var state = await _grants.ReadLifetimeAsync(player.Id, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return CommandResult.Fail(CommandFailureReason.Forbidden);
        var at = _clock().ToUniversalTime();
        var boost = _policy.ResolveGameplayBoost(_definitions, at);
        var next = _definitions.Levels.FirstOrDefault(item => item.MinimumXp > state.LifetimeXp);
        var progress = next is null ? "Highest configured level reached" : string.Create(CultureInfo.InvariantCulture,
            $"{next.MinimumXp - state.LifetimeXp} XP to level {next.Level}");
        return CommandResult.Ok(string.Create(CultureInfo.InvariantCulture,
            $"Level {state.Level.Level} | Lifetime XP: {state.LifetimeXp} | {progress} | Gameplay XP boost: {boost.Multiplier:0.####}x ({boost.BoostId ?? "none"}) at {at:yyyy-MM-dd HH:mm} UTC. Independent from rank points."));
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not interrupt other players. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _command.Dispose();
    }
}
