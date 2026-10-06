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
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _repository.ReadLifetimeAsync(playerId, cancellationToken);
    }

    public async ValueTask<ProgressionGrantResult> GrantAsync(
        PlayerId playerId,
        ProgressionGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var occurredAtUtc = NormalizeUtc(request.OccurredAt);
        var existing = await _repository.ReadGrantAsync(
            playerId, request.GrantId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!MatchesOriginalRequest(existing, request, occurredAtUtc))
                throw new ProgressionGrantConflictException(playerId, request.GrantId);

            return new ProgressionGrantResult(
                false,
                existing,
                _definitions.LevelFor(existing.LifetimeXpAfter));
        }

        var boost = _definitions.ResolveBoost(occurredAtUtc, request.Source);
        var candidate = new ProgressionGrantCandidate(
            request.GrantId,
            request.Source,
            request.BaseXp,
            _definitions.ApplyBoost(request.BaseXp, occurredAtUtc, request.Source),
            request.Reason,
            occurredAtUtc,
            boost.BoostId,
            boost.Multiplier);

        var committed = await _repository.ApplyAsync(
            playerId, candidate, cancellationToken).ConfigureAwait(false);
        return new ProgressionGrantResult(
            committed.Applied,
            committed.Grant,
            _definitions.LevelFor(committed.Grant.LifetimeXpAfter));
    }

    public static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }

    public static void ValidateRequest(ProgressionGrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PrintableBounded(request.GrantId, MaxGrantIdLength)
            || !PrintableBounded(request.Reason, MaxReasonLength)
            || request.BaseXp < 0
            || !Enum.IsDefined(request.Source))
        {
            throw new ArgumentException("Progression grant request is invalid.", nameof(request));
        }
    }

    internal static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));

    internal static bool MatchesOriginalRequest(
        ProgressionGrantRecord existing,
        ProgressionGrantRequest request,
        DateTimeOffset occurredAtUtc)
        => existing.Source == request.Source
            && existing.BaseXp == request.BaseXp
            && string.Equals(existing.Reason, request.Reason, StringComparison.Ordinal)
            && existing.OccurredAtUtc == occurredAtUtc;
}
