using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Messaging;

public sealed record MessageTarget
{
    private MessageTarget(
        MessageAudience audience,
        PlayerId? playerId,
        PlayerSessionId? sessionId,
        PlayerTeam team)
    {
        Audience = audience;
        PlayerId = playerId;
        SessionId = sessionId;
        Team = team;
    }

    public MessageAudience Audience { get; }

    public PlayerId? PlayerId { get; }

    public PlayerSessionId? SessionId { get; }

    public PlayerTeam Team { get; }

    public static MessageTarget ForPlayer(PlayerId playerId, PlayerSessionId? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return new MessageTarget(MessageAudience.Player, playerId, sessionId, PlayerTeam.Unknown);
    }

    public static MessageTarget ForTeam(PlayerTeam team)
    {
        if (team is PlayerTeam.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(team), "A concrete player team is required.");
        }

        return new MessageTarget(MessageAudience.Team, null, null, team);
    }

    public static MessageTarget ForAll()
        => new(MessageAudience.All, null, null, PlayerTeam.Unknown);
}
