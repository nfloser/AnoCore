namespace AnoCore.Abstractions.Players;

public sealed record PlayerStateUpdate
{
    public PlayerStateUpdate(
        PlayerId id,
        PlayerSessionId sessionId,
        string? name,
        PlayerTeam? team,
        bool? isAlive,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sessionId);

        Id = id;
        SessionId = sessionId;
        Name = name;
        Team = team;
        IsAlive = isAlive;
        UpdatedAtUtc = updatedAtUtc;
    }

    public PlayerId Id { get; }

    public PlayerSessionId SessionId { get; }

    public string? Name { get; }

    public PlayerTeam? Team { get; }

    public bool? IsAlive { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}
