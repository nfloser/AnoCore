using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Events;

/// <summary>
/// A connected player's non-command chat accepted by AnoCore's moderation and routing policy.
/// This observation cannot modify routing and does not confirm native client delivery.
/// </summary>
public sealed record PlayerChatAcceptedEvent(
    PlayerSnapshot Sender,
    string Message,
    bool IsTeamMessage,
    DateTimeOffset OccurredAtUtc) : IAnoEvent;

/// <summary>
/// Advisory notification started before host teardown. Asynchronous observers are not awaited
/// by the native unload callback; use module-owned lifetimes for required resource cleanup.
/// </summary>
public sealed record CoreUnloadingEvent(bool HotReload, DateTimeOffset OccurredAtUtc) : IAnoEvent;
