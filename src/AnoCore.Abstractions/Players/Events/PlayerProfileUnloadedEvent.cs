using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Players.Events;

/// <summary>Published after a leaving session profile has been durably stored.</summary>
public sealed record PlayerProfileUnloadedEvent(
    PlayerSnapshot Player,
    PlayerProfile Profile) : IAnoEvent;
