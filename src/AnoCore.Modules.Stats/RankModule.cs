using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class RankModule : IDisposable
{
    public const string MenuCommandName = "anoranks";
    private const int PageSize = 5;
    private static readonly ModuleId Owner = new("ano.ranks");

    private readonly object _menuGate = new();
    private readonly RankConfiguration _configuration;
    private readonly IPlayerRegistry _players;
    private readonly ICombatRepository _combat;
    private readonly IMenuService? _menus;
    private readonly Dictionary<PlayerId, IDisposable> _playerMenus = [];
    private readonly IDisposable _command;
    private readonly IDisposable _topCommand;
    private readonly IDisposable? _menuCommand;
    private readonly IReadOnlyList<IDisposable> _placeholders;
    private int _disposed;

    private RankModule(RankConfiguration configuration, IAnoCommandRegistry commands,
        IPlayerRegistry players, ICombatRepository combat, IMenuService? menus,
        IPlaceholderRegistry? placeholders)
    {
        _configuration = configuration;
        _players = players;
        _combat = combat;
        _menus = menus;
        _command = commands.Register(Owner,
            new CommandDescriptor("anorank", "Show your combat rank and points."),
            context => ShowRankAsync(context.Caller, context.CancellationToken));
        try
        {
            _topCommand = commands.Register(Owner,
                new CommandDescriptor("anotopranks", "Show the combat rank leaderboard.",
                    arguments:
                    [new("page", CommandArgumentKind.Int32,
                        "Page number.", required: false)]),
                ShowTopRanksAsync);
            try
            {
                _menuCommand = menus is null
                    ? null
                    : commands.Register(Owner,
                        new CommandDescriptor(MenuCommandName,
                            "Open your rank and leaderboard menu.", arguments:
                            [new("page", CommandArgumentKind.Int32,
                                "Page number.", required: false)]),
                        OpenMenuAsync);
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

        try
        {
            _placeholders = placeholders is null
                ? []
                : RegisterPlaceholders(placeholders);
        }
        catch
        {
            _menuCommand?.Dispose();
            _topCommand.Dispose();
            _command.Dispose();
            throw;
        }
    }

    public RankConfiguration Configuration => _configuration;

    public static Task<RankModule> CreateAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(configuration, commands, players, combat, null, null,
            cancellationToken);

    public static Task<RankModule> CreateAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        IPlaceholderRegistry placeholders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placeholders);
        return CreateCoreAsync(configuration, commands, players, combat, null, placeholders,
            cancellationToken);
    }

    public static Task<RankModule> CreateAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        IMenuService menus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(menus);
        return CreateCoreAsync(
            configuration, commands, players, combat, menus, null, cancellationToken);
    }

    public static Task<RankModule> CreateAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        IMenuService menus, IPlaceholderRegistry placeholders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(placeholders);
        return CreateCoreAsync(configuration, commands, players, combat, menus, placeholders,
            cancellationToken);
    }

    private static async Task<RankModule> CreateCoreAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        IMenuService? menus, IPlaceholderRegistry? placeholders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(combat);
        var settings = await configuration.LoadAsync("ranks",
            () => RankConfiguration.Default, RankConfiguration.Validate, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new RankModule(settings, commands, players, combat, menus, placeholders);
    }

    private IReadOnlyList<IDisposable> RegisterPlaceholders(
        IPlaceholderRegistry placeholders)
    {
        var registrations = new List<IDisposable>();
        try
        {
            registrations.Add(placeholders.Register(Owner, "rank.tag",
                (context, token) => ResolvePlaceholderAsync(
                    context, threshold => threshold.Tag ?? string.Empty, token)));
            registrations.Add(placeholders.RegisterPrioritized(
                Owner, "chat.tag", 0,
                (context, token) => ResolvePlaceholderAsync(
                    context, threshold => threshold.Tag ?? string.Empty, token)));
            registrations.Add(placeholders.Register(Owner, "rank.name",
                (context, token) => ResolvePlaceholderAsync(
                    context, threshold => threshold.Name, token)));
            registrations.Add(placeholders.Register(Owner, "rank.points",
                (context, token) => ResolvePointsPlaceholderAsync(context, token)));
            return registrations;
        }
        catch
        {
            foreach (var registration in registrations)
                registration.Dispose();
            throw;
        }
    }

    private async ValueTask<string?> ResolvePlaceholderAsync(
        PlaceholderContext context, Func<RankThreshold, string> selector,
        CancellationToken cancellationToken)
    {
        var points = await ResolvePointsAsync(context, cancellationToken)
            .ConfigureAwait(false);
        return points is null ? null : selector(_configuration.ForScore(points.Value));
    }

    private async ValueTask<string?> ResolvePointsPlaceholderAsync(
        PlaceholderContext context, CancellationToken cancellationToken)
    {
        var points = await ResolvePointsAsync(context, cancellationToken)
            .ConfigureAwait(false);
        return points?.ToString(CultureInfo.InvariantCulture);
    }

    private async ValueTask<long?> ResolvePointsAsync(
        PlaceholderContext context, CancellationToken cancellationToken)
    {
        var player = context.Values.FirstOrDefault(pair =>
            string.Equals(pair.Key, "player", StringComparison.OrdinalIgnoreCase)).Value;
        if (player is not PlayerId playerId)
            return null;
        var placement = await _combat.GetScorePlacementAsync(playerId,
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, cancellationToken).ConfigureAwait(false);
        return placement?.Points ?? 0;
    }

    private async ValueTask<CommandResult> ShowRankAsync(PlayerId? caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetConnected(caller, out _))
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");
        var placement = await _combat.GetScorePlacementAsync(caller!,
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, cancellationToken).ConfigureAwait(false);
        var points = placement?.Points ?? 0;
        var rank = _configuration.ForScore(points);
        var position = placement is null ? " Unranked." : $" Placement: #{placement.Position}.";
        var next = _configuration.NextAfter(points);
        var progress = next is null
            ? " Highest configured rank reached."
            : $" {next.MinimumPoints - points} point(s) to {next.Name}.";
        return CommandResult.Ok(
            $"[ANO] Rank: {rank.Name}; {points} point(s).{position}{progress}");
    }

    private async ValueTask<CommandResult> ShowTopRanksAsync(CommandContext context)
    {
        var page = Page(context);
        if (page is < 1 or > 1000)
            return InvalidPage();
        var entries = await _combat.GetTopScoresAsync(
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, PageSize, (page - 1) * PageSize,
            context.CancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
            return CommandResult.Ok("No rank entries on this page.");
        return CommandResult.Ok(string.Join(" | ", entries.Select(entry =>
        {
            var rank = _configuration.ForScore(entry.Points);
            return $"{entry.Position}. {Display(entry)}: "
                + $"{rank.Name}, {entry.Points} point(s)";
        })));
    }

    private async ValueTask<CommandResult> OpenMenuAsync(CommandContext context)
    {
        var page = Page(context);
        if (page is < 1 or > 1000)
            return InvalidPage();
        if (!TryGetConnected(context.Caller, out var player) || player is null)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");

        await OpenMenuPageAsync(player.Id, page, context.CancellationToken)
            .ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Rank menu opened.");
    }

    private async ValueTask OpenMenuPageAsync(PlayerId playerId, int page,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (page is < 1 or > 1000 || !TryGetConnected(playerId, out _))
            return;

        var placement = await _combat.GetScorePlacementAsync(playerId,
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, cancellationToken).ConfigureAwait(false);
        var entries = await _combat.GetTopScoresAsync(
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, PageSize + 1, (page - 1) * PageSize,
            cancellationToken).ConfigureAwait(false);

        if (Volatile.Read(ref _disposed) != 0
            || !TryGetConnected(playerId, out _))
            return;
        var options = BuildMenuOptions(playerId, page, placement, entries);
        var menuId = new MenuId($"ano.ranks.{playerId.SteamId64}");
        var definition = new MenuDefinition(menuId, $"Ranks — page {page}", options);
        ReplaceMenu(playerId, definition);
    }

    private IReadOnlyCollection<MenuOption> BuildMenuOptions(
        PlayerId playerId, int page, CombatScoreRankEntry? placement,
        IReadOnlyList<CombatScoreRankEntry> entries)
    {
        var points = placement?.Points ?? 0;
        var rank = _configuration.ForScore(points);
        var position = placement is null ? "unranked" : $"#{placement.Position}";
        var options = new List<MenuOption>
        {
            Info("summary", $"You: {rank.Name} — {points} points — {position}"),
        };
        var nextRank = _configuration.NextAfter(points);
        options.Add(Info("progress", nextRank is null
            ? "Highest configured rank reached"
            : $"{nextRank.MinimumPoints - points} points to {nextRank.Name}"));

        var displayed = entries.Take(PageSize).ToArray();
        if (displayed.Length == 0)
        {
            options.Add(Info("empty", "No rank entries on this page"));
        }
        else
        {
            for (var index = 0; index < displayed.Length; index++)
            {
                var entry = displayed[index];
                var entryRank = _configuration.ForScore(entry.Points);
                options.Add(Info($"entry{index + 1}",
                    $"{entry.Position}. {DisplayMenu(entry)} — "
                    + $"{entryRank.Name} — {entry.Points}"));
            }
        }

        if (page > 1)
            options.Add(Navigate("previous", "Previous page", playerId, page - 1));
        if (entries.Count > PageSize && page < 1000)
            options.Add(Navigate("next", "Next page", playerId, page + 1));
        return options;
    }

    private MenuOption Navigate(string id, string label, PlayerId playerId, int page)
        => new(id, label,
            context => OpenMenuPageAsync(playerId, page, context.CancellationToken),
            keepOpen: true);

    private static MenuOption Info(string id, string label)
        => new(id, label, _ => ValueTask.CompletedTask, keepOpen: true);

    private void ReplaceMenu(PlayerId playerId, MenuDefinition definition)
    {
        if (_menus is null) return;
        var stale = new List<IDisposable>();
        IDisposable registration;
        lock (_menuGate)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !TryGetConnected(playerId, out _))
                return;
            var online = _players.OnlinePlayers
                .Where(player => player.IsConnected)
                .Select(player => player.Id)
                .ToHashSet();
            foreach (var pair in _playerMenus
                .Where(pair => !online.Contains(pair.Key)).ToArray())
            {
                _playerMenus.Remove(pair.Key);
                stale.Add(pair.Value);
            }

            if (_playerMenus.Remove(playerId, out var previous))
                stale.Add(previous);
            foreach (var handle in stale)
                handle.Dispose();
            registration = _menus.Register(Owner, definition);
            _playerMenus[playerId] = registration;
            _menus.Open(playerId, definition.Id);
        }
    }

    private bool TryGetConnected(PlayerId? playerId, out PlayerSnapshot? player)
    {
        if (playerId is not null && _players.TryGet(playerId, out player)
            && player is not null && player.IsConnected)
            return true;
        player = null;
        return false;
    }

    private static int Page(CommandContext context)
        => context.ParsedArguments.TryGetValue("page", out var provided)
            ? (int)provided! : 1;

    private static CommandResult InvalidPage()
        => CommandResult.Fail(CommandFailureReason.InvalidInput,
            "Page must be between 1 and 1000.");

    private static string Display(CombatScoreRankEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PlayerId.SteamId64.ToString(CultureInfo.InvariantCulture)
            : $"{Sanitize(entry.DisplayName)} ({entry.PlayerId.SteamId64})";

    private static string DisplayMenu(CombatScoreRankEntry entry)
    {
        var value = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PlayerId.SteamId64.ToString(CultureInfo.InvariantCulture)
            : Sanitize(entry.DisplayName);
        return value.Length <= 48 ? value : value[..48];
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsControl(character)
            ? ' ' : character == '|' ? '/' : character));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<IDisposable> registrations;
        lock (_menuGate)
        {
            registrations = _playerMenus.Values.ToList();
            _playerMenus.Clear();
        }

        foreach (var registration in registrations)
            registration.Dispose();
        foreach (var placeholder in _placeholders)
            placeholder.Dispose();
        _menuCommand?.Dispose();
        _topCommand.Dispose();
        _command.Dispose();
    }
}
