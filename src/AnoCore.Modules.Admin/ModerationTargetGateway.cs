using System.Globalization;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;

namespace AnoCore.Modules.Admin;

public enum ModerationTargetFailure
{
    None = 0,
    EmptySelector = 1,
    SelectorNotAllowed = 2,
    NotFound = 3,
    Ambiguous = 4,
    ActorNotOnline = 5,
    PermissionDenied = 6,
    SelfTargetNotAllowed = 7,
    TargetNotOnline = 8,
    StaleTarget = 9,
    TargetImmune = 10,
}

public sealed record ModerationTarget(PlayerId Id, PlayerSnapshot? OnlinePlayer)
{
    public bool IsOnline => OnlinePlayer is not null;
}

public sealed record ModerationTargetResult(
    bool Accepted,
    ModerationTargetFailure Failure,
    ModerationTarget? Target)
{
    public static ModerationTargetResult Success(PlayerSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new ModerationTargetResult(
            true,
            ModerationTargetFailure.None,
            new ModerationTarget(player.Id, player));
    }

    public static ModerationTargetResult Success(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return new ModerationTargetResult(
            true,
            ModerationTargetFailure.None,
            new ModerationTarget(playerId, null));
    }

    public static ModerationTargetResult Reject(ModerationTargetFailure failure)
    {
        if (failure == ModerationTargetFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new ModerationTargetResult(false, failure, null);
    }
}

public sealed class ModerationTargetGateway
{
    private readonly IPlayerRegistry _players;
    private readonly IPlayerTargetResolver _resolver;
    private readonly ITargetAuthorizationService _targetAuthorization;
    private readonly IAuthorizationService _authorization;

    public ModerationTargetGateway(
        IPlayerRegistry players,
        IPlayerTargetResolver resolver,
        ITargetAuthorizationService targetAuthorization,
        IAuthorizationService authorization)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _targetAuthorization = targetAuthorization ?? throw new ArgumentNullException(nameof(targetAuthorization));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async ValueTask<ModerationTargetResult> ResolveAsync(
        string selector,
        PlayerId? actor,
        PermissionId permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = _resolver.Resolve(
            selector,
            actor,
            TargetSelectorCapabilities.None);

        if (resolved.Accepted)
        {
            var target = resolved.Targets.Single();
            if (actor is null)
            {
                return ModerationTargetResult.Success(target);
            }

            var authorization = await _targetAuthorization.AuthorizeAsync(
                actor,
                target,
                permission,
                allowSelf: false,
                cancellationToken).ConfigureAwait(false);

            return authorization.IsAllowed
                ? ModerationTargetResult.Success(target)
                : ModerationTargetResult.Reject(MapFailure(authorization.Failure));
        }

        if (resolved.Failure != TargetResolutionFailure.NotFound)
        {
            return ModerationTargetResult.Reject(MapFailure(resolved.Failure));
        }

        if (!TryParseSteamId(selector, out var offlineTarget))
        {
            return ModerationTargetResult.Reject(ModerationTargetFailure.NotFound);
        }

        if (actor is null)
        {
            return ModerationTargetResult.Success(offlineTarget);
        }

        if (!_players.TryGet(actor, out var actorSnapshot)
            || actorSnapshot is null
            || !actorSnapshot.IsConnected)
        {
            return ModerationTargetResult.Reject(ModerationTargetFailure.ActorNotOnline);
        }

        if (actor == offlineTarget)
        {
            return ModerationTargetResult.Reject(ModerationTargetFailure.SelfTargetNotAllowed);
        }

        if (!await _authorization.HasPermissionAsync(
                actor,
                permission,
                cancellationToken).ConfigureAwait(false))
        {
            return ModerationTargetResult.Reject(ModerationTargetFailure.PermissionDenied);
        }

        if (!await _authorization.CanTargetAsync(
                actor,
                offlineTarget,
                cancellationToken).ConfigureAwait(false))
        {
            return ModerationTargetResult.Reject(ModerationTargetFailure.TargetImmune);
        }

        return ModerationTargetResult.Success(offlineTarget);
    }

    private static bool TryParseSteamId(string selector, out PlayerId playerId)
    {
        playerId = null!;
        if (string.IsNullOrWhiteSpace(selector)
            || !ulong.TryParse(
                selector.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var steamId)
            || steamId == 0)
        {
            return false;
        }

        playerId = new PlayerId(steamId);
        return true;
    }

    private static ModerationTargetFailure MapFailure(TargetResolutionFailure failure)
        => failure switch
        {
            TargetResolutionFailure.EmptySelector => ModerationTargetFailure.EmptySelector,
            TargetResolutionFailure.SelectorNotAllowed => ModerationTargetFailure.SelectorNotAllowed,
            TargetResolutionFailure.NotFound => ModerationTargetFailure.NotFound,
            TargetResolutionFailure.Ambiguous => ModerationTargetFailure.Ambiguous,
            TargetResolutionFailure.CallerRequired => ModerationTargetFailure.SelectorNotAllowed,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

    private static ModerationTargetFailure MapFailure(TargetAuthorizationFailure failure)
        => failure switch
        {
            TargetAuthorizationFailure.ActorNotOnline => ModerationTargetFailure.ActorNotOnline,
            TargetAuthorizationFailure.PermissionDenied => ModerationTargetFailure.PermissionDenied,
            TargetAuthorizationFailure.SelfTargetNotAllowed => ModerationTargetFailure.SelfTargetNotAllowed,
            TargetAuthorizationFailure.TargetNotOnline => ModerationTargetFailure.TargetNotOnline,
            TargetAuthorizationFailure.StaleTarget => ModerationTargetFailure.StaleTarget,
            TargetAuthorizationFailure.TargetImmune => ModerationTargetFailure.TargetImmune,
            TargetAuthorizationFailure.None => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };
}
