using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Moderation;

public interface IModerationSnapshotProvider
{
    bool TryGetRestrictions(
        PlayerId targetId,
        DateTimeOffset atUtc,
        out ModerationRestriction restrictions);

    void Invalidate(PlayerId targetId);
}
