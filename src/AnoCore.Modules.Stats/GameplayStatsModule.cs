using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class GameplayStatsModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly IGameplayStatRepository _repository;
    private readonly GameplayStatsConfiguration _configuration;
    private readonly IDisposable _command;
    private int _disposed;

    public GameplayStatsModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        IGameplayStatRepository repository, GameplayStatsConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _configuration = configuration ?? GameplayStatsConfiguration.Default;
        var configurationErrors = GameplayStatsConfiguration.Validate(_configuration);
        if (configurationErrors.Count != 0)
            throw new ArgumentException(string.Join(" ", configurationErrors), nameof(configuration));
        _command = commands.Register(
            new ModuleId("ano.stats"),
            new CommandDescriptor(
                "anogamestats",
                "Show your persisted gameplay statistics.",
                arguments:
                [new("map", CommandArgumentKind.String, "Optional map name.", required: false)]),
            OwnStatsAsync);
    }

    public GameplayStatsConfiguration Configuration => _configuration;

    public static async Task<GameplayStatsModule> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        IGameplayStatRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = await configuration.LoadAsync(
            "gameplay-stats",
            () => GameplayStatsConfiguration.Default,
            GameplayStatsConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new GameplayStatsModule(commands, players, repository, settings);
    }

    public ValueTask RecordAsync(GameplayStatEvent statistic,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(statistic);
        return _repository.RecordAsync(statistic, cancellationToken);
    }

    private async ValueTask<CommandResult> OwnStatsAsync(CommandContext context)
    {
        if (context.Caller is null
            || !_players.TryGet(context.Caller, out var player)
            || player is null
            || !player.IsConnected)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");
        }

        GameplayStatFilter filter;
        try
        {
            filter = new GameplayStatFilter(
                context.Arguments.Count > 0 ? context.Arguments[0] : null);
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }

        var sessionId = player.SessionId;
        var totals = await _repository.ReadAsync(
            context.Caller, filter, context.CancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return CommandResult.Fail(CommandFailureReason.NotFound,
                "Gameplay statistics are no longer available.");
        }

        if (!_players.TryGet(context.Caller, out var current)
            || current is null
            || !current.IsConnected
            || current.SessionId != sessionId)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Player session changed while statistics were loading.");
        }

        if (totals.Count == 0)
            return CommandResult.Ok("[ANO] Gameplay stats: no recorded events.");

        return CommandResult.Ok("[ANO] Gameplay stats: "
            + string.Join(", ", totals.Select(entry => $"{entry.Kind}={entry.Count}")));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _command.Dispose();
    }
}
