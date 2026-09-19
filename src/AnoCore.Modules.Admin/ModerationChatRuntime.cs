using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;

namespace AnoCore.Modules.Admin;

public sealed class ModerationChatRuntime : IDisposable
{
    private ModerationSnapshotLifecycle? _lifecycle;

    public ModerationChatRuntime(
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
        Gate = new ModerationChatGate(
            new ModerationCommunicationPolicy(snapshots, clock));
    }

    public ModerationChatGate Gate { get; }

    public void Dispose()
        => Interlocked.Exchange(ref _lifecycle, null)?.Dispose();
}
