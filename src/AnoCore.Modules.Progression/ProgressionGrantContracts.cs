using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed record ProgressionLifetimeState(
    PlayerId PlayerId,
    long LifetimeXp,
    long Revision);

public sealed record ProgressionGrantRequest(
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record ProgressionGrantCandidate(
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    long AwardedXp,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    string? BoostId,
    decimal BoostMultiplier);

public sealed record ProgressionGrantRecord(
    PlayerId PlayerId,
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    long AwardedXp,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    string? BoostId,
    decimal BoostMultiplier,
    long LifetimeXpAfter,
    long AccountRevisionAfter);

public sealed record ProgressionGrantCommitResult(
    bool Applied,
    ProgressionGrantRecord Grant);

public sealed record ProgressionGrantResult(
    bool Applied,
    ProgressionGrantRecord Grant,
    XpLevelThreshold LevelAfter);

public interface IProgressionGrantRepository
{
    ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);

    ValueTask<ProgressionGrantRecord?> ReadGrantAsync(
        PlayerId playerId,
        string grantId,
        CancellationToken cancellationToken = default);

    ValueTask<ProgressionGrantCommitResult> ApplyAsync(
        PlayerId playerId,
        ProgressionGrantCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class ProgressionGrantConflictException : InvalidOperationException
{
    public ProgressionGrantConflictException(PlayerId playerId, string grantId)
        : base($"Progression grant '{grantId}' conflicts with an existing grant for player {playerId}.")
    {
        PlayerId = playerId;
        GrantId = grantId;
    }

    public PlayerId PlayerId { get; }

    public string GrantId { get; }
}

public sealed class ProgressionGrantService
{
    public const int MaxGrantIdLength = 128;
    public const int MaxReasonLength = 128;

    private readonly IProgressionGrantRepository _repository;
    private readonly ProgressionDefinitionSnapshot _definitions;

    public ProgressionGrantService(
        IProgressionGrantRepository repository,
        ProgressionDefinitionSnapshot definitions)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    }

    public ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<ProgressionGrantResult> GrantAsync(
        PlayerId playerId,
        ProgressionGrantRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
