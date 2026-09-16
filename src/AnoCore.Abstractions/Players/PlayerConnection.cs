namespace AnoCore.Abstractions.Players;

public sealed record PlayerConnection
{
    public PlayerConnection(
        PlayerId id,
        string name,
        PlayerTeam team,
        bool isAlive,
        DateTimeOffset connectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
        Name = name;
        Team = team;
        IsAlive = isAlive;
        ConnectedAtUtc = connectedAtUtc;
    }

    public PlayerId Id { get; }

    public string Name { get; }

    public PlayerTeam Team { get; }

    public bool IsAlive { get; }

    public DateTimeOffset ConnectedAtUtc { get; }
}
