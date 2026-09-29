using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Players.Events;

/// <summary>Published after the current session profile has been durably stored.</summary>
public sealed record PlayerProfileLoadedEvent(
    PlayerSnapshot Player,
    PlayerProfile Profile) : IAnoEvent;
