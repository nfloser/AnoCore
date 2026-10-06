using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed class GameplayXpModule : IDisposable
{
    private readonly GameplayXpPolicy _policy;
    private readonly ProgressionDefinitionSnapshot _definitions;
    private readonly IPlayerRegistry _players;
    private readonly IGameplayXpRepository _repository;
    private readonly ProgressionGrantService _grants;
    private readonly Action<Exception>? _reportError;
    private readonly IDisposable _command;
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public GameplayXpModule(GameplayXpPolicy policy, ProgressionDefinitionSnapshot definitions,
        IPlayerRegistry players, IGameplayXpRepository repository, IProgressionGrantRepository grants,
        IAnoCommandRegistry commands, Action<Exception>? reportError = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _grants = new ProgressionGrantService(grants, definitions);
        ArgumentNullException.ThrowIfNull(commands);
        _reportError = reportError;
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
                    await _repository.ReconcileAsync(player.Id, _policy, _definitions, at, linked.Token).ConfigureAwait(false);
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
        return CommandResult.Ok($"Level {state.Level.Level} | Lifetime XP: {state.LifetimeXp}. Independent from rank points.");
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
