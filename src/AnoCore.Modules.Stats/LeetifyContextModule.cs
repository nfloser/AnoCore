using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Stats;

public sealed class LeetifyContextModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly ILeetifyProfileProvider _provider;
    private readonly IDisposable _command;
    private int _disposed;

    public LeetifyContextModule(
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        ILeetifyProfileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _command = commands.Register(
            new ModuleId("ano.rating.leetify"),
            new CommandDescriptor(
                "anoleetify",
                "Show live Leetify context for a connected player.",
                arguments:
                [
                    new(
                        "player",
                        CommandArgumentKind.String,
                        "Connected player name or SteamID64.",
                        required: true),
                ]),
            ExecuteAsync);
    }

    private async ValueTask<CommandResult> ExecuteAsync(CommandContext context)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return Unavailable();

        PlayerSnapshot? caller = null;
        if (context.Caller is not null
            && (!_players.TryGet(context.Caller, out caller)
                || caller is not { IsConnected: true }))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A connected caller is required.");
        }

        if (context.Arguments.Count != 1)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Use !anoleetify <player>.");

        var query = context.Arguments[0];
        if (query.Length is < 1 or > 128 || query.Any(char.IsControl))
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Invalid player selector.");

        var online = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64)
            .Take(129)
            .ToArray();
        if (online.Length > 128)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Leetify context supports at most 128 connected players.");

        var targets = Resolve(online, query);
        if (targets.Length != 1)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                targets.Length == 0
                    ? "No connected player matches."
                    : "Player selector is ambiguous; use SteamID64.");
        }

        var target = targets[0];
        var result = await _provider.ReadAsync(
            target.Id,
            context.CancellationToken).ConfigureAwait(false);

        if (!Current(target) || !CallerCurrent(caller))
            return SessionChanged();

        if (result.Status != LeetifyLookupStatus.Available
            || result.Profile is null)
        {
            return ProviderFailure(result.Status);
        }

        if (!ValidProfile(result.Profile, target.Id))
            return ProviderFailure(LeetifyLookupStatus.InvalidResponse);

        return CommandResult.Ok(
            $"[ANO] Data Provided by Leetify — {SafeName(target)}: "
            + string.Join(
                ", ",
                result.Profile.Metrics.Select(metric =>
                    $"{metric.Name}={metric.Value}"))
            + $". View on Leetify: {result.Profile.ProfileUri.AbsoluteUri}");
    }

    private static bool ValidProfile(
        LeetifyProfileContext profile,
        PlayerId player)
    {
        if (profile.Player != player
            || profile.Metrics.Count != 3
            || !string.Equals(
                profile.ProfileUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                profile.ProfileUri.Host,
                "leetify.com",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                profile.ProfileUri.AbsolutePath.TrimEnd('/'),
                $"/app/profile/{player.SteamId64}",
                StringComparison.Ordinal))
        {
            return false;
        }

        var expected = new[] { "Aim", "Positioning", "Utility" };
        for (var index = 0; index < expected.Length; index++)
        {
            var metric = profile.Metrics[index];
            if (!string.Equals(metric.Name, expected[index], StringComparison.Ordinal)
                || metric.Value.Length is < 1 or > 64
                || metric.Value.Any(char.IsControl)
                || !double.TryParse(
                    metric.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !double.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static PlayerSnapshot[] Resolve(
        IReadOnlyList<PlayerSnapshot> online,
        string query)
    {
        if (ulong.TryParse(
            query,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var steam))
        {
            return online
                .Where(player => player.Id.SteamId64 == steam)
                .ToArray();
        }

        var exact = online
            .Where(player => string.Equals(
                player.Name,
                query,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return exact.Length != 0
            ? exact
            : online
                .Where(player => player.Name.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private bool CallerCurrent(PlayerSnapshot? caller)
        => caller is null || Current(caller);

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0
            && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true }
            && current.SessionId == player.SessionId;

    private static string SafeName(PlayerSnapshot player)
        => new(player.Name
            .Where(character => !char.IsControl(character)
                && character is not '{' and not '}' and not '|')
            .Take(24)
            .ToArray());

    private static CommandResult ProviderFailure(LeetifyLookupStatus status)
        => CommandResult.Fail(
            CommandFailureReason.NotFound,
            status switch
            {
                LeetifyLookupStatus.NotFound
                    => "Leetify data is not available for this player.",
                LeetifyLookupStatus.Private
                    => "This player's Leetify profile is private.",
                LeetifyLookupStatus.RateLimited
                    => "Leetify is rate limited; try again later.",
                LeetifyLookupStatus.Unauthorized
                    => "Leetify integration is unavailable.",
                LeetifyLookupStatus.Timeout
                    => "Leetify request timed out; internal AnoRating remains available.",
                LeetifyLookupStatus.InvalidResponse
                    => "Leetify returned an unsupported response.",
                _ => "Leetify is unavailable; internal AnoRating remains available.",
            });

    private static CommandResult SessionChanged()
        => CommandResult.Fail(
            CommandFailureReason.InvalidInput,
            "Player session changed while Leetify data was loading.");

    private static CommandResult Unavailable()
        => CommandResult.Fail(
            CommandFailureReason.NotFound,
            "Leetify context is no longer available.");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _command.Dispose();
    }
}
