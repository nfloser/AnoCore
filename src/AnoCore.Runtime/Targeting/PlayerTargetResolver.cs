using System.Globalization;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;

namespace AnoCore.Runtime.Targeting;

public sealed class PlayerTargetResolver : IPlayerTargetResolver
{
    private readonly IPlayerRegistry _players;

    public PlayerTargetResolver(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public TargetResolutionResult Resolve(
        string selector,
        PlayerId? caller = null,
        TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.None)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return TargetResolutionResult.Reject(TargetResolutionFailure.EmptySelector);
        }

        var normalized = selector.Trim();
        var online = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64)
            .ToArray();

        if (normalized.StartsWith('@'))
        {
            return ResolveSpecial(normalized, caller, capabilities, online);
        }

        if (ulong.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId)
            && steamId != 0)
        {
            var bySteamId = online.FirstOrDefault(player => player.Id.SteamId64 == steamId);
            return bySteamId is null
                ? TargetResolutionResult.Reject(TargetResolutionFailure.NotFound)
                : TargetResolutionResult.Success([bySteamId]);
        }

        var exact = online
            .Where(player => string.Equals(player.Name, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1)
        {
            return TargetResolutionResult.Success(exact);
        }

        if (exact.Length > 1)
        {
            return TargetResolutionResult.Reject(TargetResolutionFailure.Ambiguous);
        }

        var prefix = online
            .Where(player => player.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return prefix.Length switch
        {
            0 => TargetResolutionResult.Reject(TargetResolutionFailure.NotFound),
            1 => TargetResolutionResult.Success(prefix),
            _ => TargetResolutionResult.Reject(TargetResolutionFailure.Ambiguous),
        };
    }

    private static TargetResolutionResult ResolveSpecial(
        string selector,
        PlayerId? caller,
        TargetSelectorCapabilities capabilities,
        IReadOnlyList<PlayerSnapshot> online)
    {
        switch (selector.ToLowerInvariant())
        {
            case "@me":
                if (!capabilities.HasFlag(TargetSelectorCapabilities.Self))
                {
                    return TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);
                }

                if (caller is null)
                {
                    return TargetResolutionResult.Reject(TargetResolutionFailure.CallerRequired);
                }

                var self = online.FirstOrDefault(player => player.Id == caller);
                return self is null
                    ? TargetResolutionResult.Reject(TargetResolutionFailure.NotFound)
                    : TargetResolutionResult.Success([self]);

            case "@all":
                return capabilities.HasFlag(TargetSelectorCapabilities.All)
                    ? FromMultiTarget(online)
                    : TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);

            case "@t":
                return capabilities.HasFlag(TargetSelectorCapabilities.Team)
                    ? FromMultiTarget(online.Where(player => player.Team == PlayerTeam.Terrorist))
                    : TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);

            case "@ct":
                return capabilities.HasFlag(TargetSelectorCapabilities.Team)
                    ? FromMultiTarget(online.Where(player => player.Team == PlayerTeam.CounterTerrorist))
                    : TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);

            case "@spec":
                return capabilities.HasFlag(TargetSelectorCapabilities.Team)
                    ? FromMultiTarget(online.Where(player => player.Team == PlayerTeam.Spectator))
                    : TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);

            default:
                return TargetResolutionResult.Reject(TargetResolutionFailure.SelectorNotAllowed);
        }
    }

    private static TargetResolutionResult FromMultiTarget(IEnumerable<PlayerSnapshot> targets)
    {
        var materialized = targets.OrderBy(player => player.Id.SteamId64).ToArray();
        return materialized.Length == 0
            ? TargetResolutionResult.Reject(TargetResolutionFailure.NotFound)
            : TargetResolutionResult.Success(materialized);
    }
}
