namespace AnoCore.Abstractions.Players;

public interface IAnoPlayer
{
    PlayerId Id { get; }

    string Name { get; }

    bool IsConnected { get; }

    bool IsAlive { get; }
}
