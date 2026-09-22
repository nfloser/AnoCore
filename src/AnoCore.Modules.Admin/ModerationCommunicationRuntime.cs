using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ModerationCommunicationRuntime : IDisposable
{
    private ModerationSnapshotLifecycle? _lifecycle;

    public ModerationCommunicationRuntime(
        IAnoEventBus events,
        IModerationService moderation,
        IModerationSnapshotProvider snapshots,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(moderation);
        ArgumentNullException.ThrowIfNull(snapshots);

        var clock = timeProvider ?? TimeProvider.System;
        _lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            snapshots,
            clock);
        Policy = new ModerationCommunicationPolicy(snapshots, clock);
        ChatGate = new ModerationChatGate(Policy);
    }

    public IModerationCommunicationPolicy Policy { get; }

    public ModerationChatGate ChatGate { get; }

    public ValueTask WarmExistingAsync(
        IEnumerable<PlayerSnapshot> players,
        CancellationToken cancellationToken = default)
    {
        var lifecycle = Volatile.Read(ref _lifecycle);
        ObjectDisposedException.ThrowIf(lifecycle is null, this);
        return lifecycle.WarmExistingAsync(players, cancellationToken);
    }

    public void Dispose()
        => Interlocked.Exchange(ref _lifecycle, null)?.Dispose();
}
