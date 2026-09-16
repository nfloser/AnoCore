namespace AnoCore.Abstractions.Players;

public sealed record PlayerSnapshot : IAnoPlayer
{
    public PlayerSnapshot(
        PlayerId id,
        PlayerSessionId sessionId,
        string name,
        bool isConnected,
        bool isAlive,
        PlayerTeam team,
        DateTimeOffset connectedAtUtc,
        DateTimeOffset lastUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sessionId);

        Id = id;
        SessionId = sessionId;
        Name = NormalizeName(name);
        IsConnected = isConnected;
        IsAlive = isAlive;
        Team = team;
        ConnectedAtUtc = connectedAtUtc;
        LastUpdatedAtUtc = lastUpdatedAtUtc < connectedAtUtc ? connectedAtUtc : lastUpdatedAtUtc;
    }

    public PlayerId Id { get; }

    public PlayerSessionId SessionId { get; }

    public string Name { get; }

    public bool IsConnected { get; }

    public bool IsAlive { get; }

    public PlayerTeam Team { get; }

    public DateTimeOffset ConnectedAtUtc { get; }

    public DateTimeOffset LastUpdatedAtUtc { get; }

    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Unknown" : name.Trim();
}
