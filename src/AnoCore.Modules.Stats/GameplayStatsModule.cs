using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class GameplayStatsModule : IDisposable
{
    public const string MenuCommandName = "anostatsmenu";
    private const int MenuPageSize = 5;
    private static readonly ModuleId Owner = new("ano.stats");

    private readonly object _menuGate = new();
    private readonly IPlayerRegistry _players;
    private readonly IGameplayStatRepository _repository;
    private readonly ICombatRepository? _combat;
    private readonly ICombatDetailRepository? _details;
    private readonly IMenuService? _menus;
    private readonly GameplayStatsConfiguration _configuration;
    private readonly Dictionary<PlayerId, MenuRegistration> _playerMenus = [];
    private readonly IDisposable[] _subscriptions;
    private readonly IDisposable _command;
    private readonly IDisposable? _menuCommand;
    private int _generation;
    private int _disposed;

    public GameplayStatsModule(
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        IGameplayStatRepository repository,
        GameplayStatsConfiguration? configuration = null,
        ICombatRepository? combat = null,
        IMenuService? menus = null,
        IAnoEventBus? events = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _combat = combat;
        _details = combat as ICombatDetailRepository;
        _menus = menus;
        _configuration = configuration ?? GameplayStatsConfiguration.Default;
        var configurationErrors = GameplayStatsConfiguration.Validate(_configuration);
        if (configurationErrors.Count != 0)
            throw new ArgumentException(string.Join(" ", configurationErrors), nameof(configuration));

        var commandsRegistered = new List<IDisposable>();
        var subscriptions = new List<IDisposable>();
        try
        {
            _command = commands.Register(
                Owner,
                new CommandDescriptor(
                    "anogamestats",
                    "Show your persisted gameplay statistics.",
                    arguments:
                    [new("map", CommandArgumentKind.String, "Optional map name.", required: false)]),
                OwnStatsAsync);
            commandsRegistered.Add(_command);

            if (menus is not null && combat is not null)
            {
                _menuCommand = commands.Register(
                    Owner,
                    new CommandDescriptor(
                        MenuCommandName,
                        "Open your persisted statistics overview.",
                        arguments:
                        [
                            new("map", CommandArgumentKind.String, "Optional map name.", required: false),
                            new("weapon", CommandArgumentKind.String, "Optional weapon name.", required: false),
                        ]),
                    OpenMenuAsync);
                commandsRegistered.Add(_menuCommand);
            }

            if (events is not null)
            {
                subscriptions.Add(events.Subscribe<PlayerDisconnectedEvent>(
                    (value, _) =>
                    {
                        RemoveSessionMenu(value.Player);
                        return ValueTask.CompletedTask;
                    }));
                subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(
                    (value, _) =>
                    {
                        RemoveSessionMenu(value.Previous);
                        return ValueTask.CompletedTask;
                    }));
            }

            _subscriptions = subscriptions.ToArray();
        }
        catch
        {
            foreach (var subscription in subscriptions)
                subscription.Dispose();
            foreach (var registration in commandsRegistered)
                registration.Dispose();
            throw;
        }
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
        var settings = await LoadConfigurationAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        return new GameplayStatsModule(commands, players, repository, settings);
    }

    public static async Task<GameplayStatsModule> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        IGameplayStatRepository repository,
        ICombatRepository combat,
        IMenuService menus,
        IAnoEventBus events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(events);
        var settings = await LoadConfigurationAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        return new GameplayStatsModule(
            commands, players, repository, settings, combat, menus, events);
    }

    private static async Task<GameplayStatsConfiguration> LoadConfigurationAsync(
        IConfigStore configuration,
        CancellationToken cancellationToken)
    {
        var settings = await configuration.LoadAsync(
            "gameplay-stats",
            () => GameplayStatsConfiguration.Default,
            GameplayStatsConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return settings;
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
        if (!TryGetConnected(context.Caller, out var player) || player is null)
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
            player.Id, filter, context.CancellationToken).ConfigureAwait(false);
        if (!IsCurrentSession(player.Id, sessionId))
            return SessionChanged();

        if (totals.Count == 0)
            return CommandResult.Ok("[ANO] Gameplay stats: no recorded events.");

        return CommandResult.Ok("[ANO] Gameplay stats: "
            + string.Join(", ", totals.Select(entry => $"{entry.Kind}={entry.Count}")));
    }

    private async ValueTask<CommandResult> OpenMenuAsync(CommandContext context)
    {
        if (_menus is null || _combat is null
            || !TryGetConnected(context.Caller, out var player) || player is null)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");
        }

        if (!TryFilters(context, out var detailFilter, out var gameplayFilter, out var failure))
            return failure!;

        var opened = await OpenMenuPageAsync(
            player, 1, detailFilter, gameplayFilter, context.CancellationToken)
            .ConfigureAwait(false);
        if (!opened)
            return SessionChanged();

        return CommandResult.Ok("[ANO] Statistics menu opened.");
    }

    private async ValueTask<bool> OpenMenuPageAsync(
        PlayerSnapshot player,
        int page,
        CombatDetailFilter detailFilter,
        GameplayStatFilter gameplayFilter,
        CancellationToken cancellationToken)
    {
        if (_menus is null || _combat is null || page is < 1 or > 1000
            || !TryCurrentSession(player))
            return false;

        var combat = await _combat.ReadAsync(player.Id, cancellationToken).ConfigureAwait(false);
        if (!TryCurrentSession(player))
            return false;

        CombatDetailTotals? detail = null;
        IReadOnlyList<CombatHitgroupTotals> hitgroups = [];
        if (_details is not null)
        {
            detail = await _details.ReadDetailsAsync(
                player.Id, detailFilter, cancellationToken).ConfigureAwait(false);
            if (!TryCurrentSession(player))
                return false;

            hitgroups = await _details.ReadHitgroupsAsync(
                player.Id, detailFilter, cancellationToken).ConfigureAwait(false);
            if (!TryCurrentSession(player))
                return false;
        }

        var gameplay = await _repository.ReadAsync(
            player.Id, gameplayFilter, cancellationToken).ConfigureAwait(false);
        if (!TryCurrentSession(player))
            return false;

        var lines = BuildMenuLines(combat, detail, hitgroups, gameplay);
        var start = (page - 1) * MenuPageSize;
        if (start >= lines.Count)
            return false;

        var generation = Interlocked.Increment(ref _generation).ToString("x8");
        var options = lines.Skip(start).Take(MenuPageSize)
            .Select((line, index) => new MenuOption(
                $"s{generation}_{index}",
                line,
                static _ => ValueTask.CompletedTask,
                keepOpen: true))
            .ToList();

        if (page > 1)
        {
            options.Add(new MenuOption(
                $"p{generation}",
                "Previous page",
                async selection =>
                {
                    await OpenMenuPageAsync(
                        player, page - 1, detailFilter, gameplayFilter,
                        selection.CancellationToken).ConfigureAwait(false);
                },
                keepOpen: true));
        }

        if (lines.Count > start + MenuPageSize && page < 1000)
        {
            options.Add(new MenuOption(
                $"n{generation}",
                "Next page",
                async selection =>
                {
                    await OpenMenuPageAsync(
                        player, page + 1, detailFilter, gameplayFilter,
                        selection.CancellationToken).ConfigureAwait(false);
                },
                keepOpen: true));
        }

        var definition = new MenuDefinition(
            new MenuId($"ano.stats.{player.Id.SteamId64}"),
            $"Statistics — page {page}",
            options);

        lock (_menuGate)
        {
            if (!TryCurrentSession(player))
                return false;
            if (_playerMenus.Remove(player.Id, out var previous))
                previous.Handle.Dispose();
            var handle = _menus.Register(Owner, definition);
            _playerMenus[player.Id] = new MenuRegistration(player.SessionId, handle);
            _menus.Open(player.Id, definition.Id);
            return true;
        }
    }

    private static IReadOnlyList<string> BuildMenuLines(
        CombatTotals combat,
        CombatDetailTotals? detail,
        IReadOnlyList<CombatHitgroupTotals> hitgroups,
        IReadOnlyList<GameplayStatTotal> gameplay)
    {
        var lines = new List<string>
        {
            $"K/D/A: {combat.Kills}/{combat.Deaths}/{combat.Assists}",
        };

        if (detail is not null)
        {
            lines.Add($"Shots/Hits: {detail.Shots}/{detail.Hits}");
            lines.Add($"Damage: {detail.DamageHealth} HP / {detail.DamageArmor} armor");
            lines.Add($"Head hits: {detail.HeadHits}");

            foreach (var entry in hitgroups.OrderBy(x => x.Hitgroup).Take(16))
                lines.Add($"HG{entry.Hitgroup}: {entry.Hits} hit(s), {entry.DamageHealth} damage");
        }

        if (gameplay.Count == 0)
        {
            lines.Add("Gameplay: no recorded events");
        }
        else
        {
            foreach (var entry in gameplay.OrderBy(x => x.Kind))
                lines.Add($"{entry.Kind}: {entry.Count}");
        }

        return lines;
    }

    private static bool TryFilters(
        CommandContext context,
        out CombatDetailFilter detail,
        out GameplayStatFilter gameplay,
        out CommandResult? failure)
    {
        var map = context.Arguments.Count > 0 ? context.Arguments[0] : null;
        var weapon = context.Arguments.Count > 1 ? context.Arguments[1] : null;
        try
        {
            detail = new CombatDetailFilter(map, weapon);
            gameplay = new GameplayStatFilter(map);
            failure = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            detail = new CombatDetailFilter();
            gameplay = new GameplayStatFilter();
            failure = CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
            return false;
        }
    }

    private bool TryGetConnected(PlayerId? id, out PlayerSnapshot? player)
    {
        player = null;
        return Volatile.Read(ref _disposed) == 0
            && id is not null
            && _players.TryGet(id, out player)
            && player is { IsConnected: true };
    }

    private bool TryCurrentSession(PlayerSnapshot player)
        => IsCurrentSession(player.Id, player.SessionId);

    private bool IsCurrentSession(PlayerId id, PlayerSessionId sessionId)
        => Volatile.Read(ref _disposed) == 0
            && _players.TryGet(id, out var current)
            && current is { IsConnected: true }
            && current.SessionId == sessionId;

    private static CommandResult SessionChanged()
        => CommandResult.Fail(CommandFailureReason.InvalidInput,
            "Player session changed while statistics were loading.");

    private void RemoveSessionMenu(PlayerSnapshot player)
    {
        lock (_menuGate)
        {
            if (_playerMenus.TryGetValue(player.Id, out var registration)
                && registration.SessionId == player.SessionId)
            {
                _playerMenus.Remove(player.Id);
                registration.Handle.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        lock (_menuGate)
        {
            foreach (var registration in _playerMenus.Values)
                registration.Handle.Dispose();
            _playerMenus.Clear();
        }

        _menuCommand?.Dispose();
        _command.Dispose();
    }

    private sealed record MenuRegistration(PlayerSessionId SessionId, IDisposable Handle);
}
