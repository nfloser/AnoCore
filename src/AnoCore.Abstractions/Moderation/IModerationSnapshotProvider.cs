using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Moderation;

public interface IModerationSnapshotProvider
{
    bool TryGetRestrictions(
        PlayerId targetId,
        DateTimeOffset atUtc,
        out ModerationRestriction restrictions);

    ValueTask InvalidateAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default);
}
