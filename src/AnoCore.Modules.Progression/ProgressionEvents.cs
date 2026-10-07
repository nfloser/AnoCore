using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

/// <summary>A newly committed lifetime grant. Delivery is best effort, not a durable outbox.</summary>
public sealed record ProgressionXpGrantedEvent(PlayerSnapshot Player, ProgressionGrantRecord Grant) : IAnoEvent;

/// <summary>A lifetime level transition derived from one committed grant and the accepted curve.</summary>
public sealed record ProgressionLevelUpEvent(PlayerSnapshot Player, ProgressionGrantRecord Grant,
    int PreviousLevel, int Level) : IAnoEvent;

public sealed record ChallengeCompletedEvent(PlayerSnapshot Player, ChallengeCompletionRecord Completion) : IAnoEvent;

public sealed record AchievementUnlockedEvent(PlayerSnapshot Player, AchievementUnlockRecord Unlock) : IAnoEvent;

/// <summary>Called only with newly committed rewards; never retries persistence or observer delivery.</summary>
public sealed class ProgressionEventPublisher(IAnoEventBus events, ProgressionDefinitionSnapshot definitions,
    Action<Exception>? reportError = null)
{
    private readonly IAnoEventBus _events = events ?? throw new ArgumentNullException(nameof(events));
    private readonly ProgressionDefinitionSnapshot _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public async ValueTask GrantAsync(PlayerSnapshot player, ProgressionGrantRecord grant,
        CancellationToken cancellationToken = default)
    {
        if (grant.PlayerId != player.Id || grant.AwardedXp < 0 || grant.LifetimeXpAfter < grant.AwardedXp)
            throw new ArgumentException("A committed reward must belong to its player and have valid lifetime totals.");
        var previous = _definitions.LevelFor(grant.LifetimeXpAfter - grant.AwardedXp).Level;
        var current = _definitions.LevelFor(grant.LifetimeXpAfter).Level;
        await PublishAsync(new ProgressionXpGrantedEvent(player, grant), cancellationToken).ConfigureAwait(false);
        if (current > previous)
            await PublishAsync(new ProgressionLevelUpEvent(player, grant, previous, current), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ChallengeAsync(PlayerSnapshot player, ChallengeCompletionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!result.Applied || result.Completion is not { } completion) return;
        await GrantAsync(player, completion.Grant, cancellationToken).ConfigureAwait(false);
        await PublishAsync(new ChallengeCompletedEvent(player, completion), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AchievementAsync(PlayerSnapshot player, AchievementUnlockRecord unlock,
        CancellationToken cancellationToken = default)
    {
        await GrantAsync(player, unlock.Grant, cancellationToken).ConfigureAwait(false);
        await PublishAsync(new AchievementUnlockedEvent(player, unlock), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishAsync<T>(T value, CancellationToken cancellationToken) where T : IAnoEvent
    {
        try { await _events.PublishAsync(value, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            try { reportError?.Invoke(exception); }
            catch { /* Observer diagnostics must not interrupt other committed rewards. */ }
        }
    }
}
