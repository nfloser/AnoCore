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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _command.Dispose();
    }
}
