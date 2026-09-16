namespace AnoCore.Abstractions.Players;

public interface IAnoPlayer
{
    PlayerId Id { get; }

    PlayerSessionId SessionId { get; }

    string Name { get; }

    bool IsConnected { get; }

    bool IsAlive { get; }

    PlayerTeam Team { get; }

    DateTimeOffset ConnectedAtUtc { get; }

    DateTimeOffset LastUpdatedAtUtc { get; }
}
