using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed record SeasonProgressionState(
    PlayerId PlayerId,
    string SeasonId,
    int SeasonVersion,
    long SeasonXp,
    long Revision);

public sealed record SeasonProgressionResult(
    PlayerId PlayerId,
    string SeasonId,
    int SeasonVersion,
    long SeasonXp,
    long Revision,
    XpLevelThreshold Level);

public sealed record SeasonXpGrantRequest(
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record SeasonXpGrantCandidate(
    string SeasonId,
    int SeasonVersion,
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    long AwardedXp,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    string? BoostId,
    decimal BoostMultiplier);

public sealed record SeasonXpGrantRecord(
    PlayerId PlayerId,
    string SeasonId,
    int SeasonVersion,
    string GrantId,
    ProgressionXpSource Source,
    long BaseXp,
    long AwardedXp,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    string? BoostId,
    decimal BoostMultiplier,
    long SeasonXpAfter,
    long AccountRevisionAfter);

public sealed record SeasonXpGrantCommitResult(
    bool Applied,
    SeasonXpGrantRecord Grant);

public sealed record SeasonXpGrantResult(
    bool Applied,
    SeasonXpGrantRecord Grant,
    XpLevelThreshold LevelAfter);

public interface ISeasonProgressionRepository
{
    ValueTask<SeasonProgressionState> ReadAsync(
        PlayerId playerId,
        string seasonId,
        int seasonVersion,
        CancellationToken cancellationToken = default);

    ValueTask<SeasonXpGrantRecord?> ReadGrantAsync(
        PlayerId playerId,
        string seasonId,
        string grantId,
        CancellationToken cancellationToken = default);

    ValueTask<SeasonXpGrantCommitResult> ApplyAsync(
        PlayerId playerId,
        SeasonXpGrantCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class SeasonXpGrantConflictException : InvalidOperationException
{
    public SeasonXpGrantConflictException(
        PlayerId playerId,
        string seasonId,
        string grantId)
        : base($"Season XP grant '{grantId}' conflicts with an existing grant for player {playerId} in season '{seasonId}'.")
    {
        PlayerId = playerId;
        SeasonId = seasonId;
        GrantId = grantId;
    }

    public PlayerId PlayerId { get; }
    public string SeasonId { get; }
    public string GrantId { get; }
}

public sealed class SeasonNotActiveException : InvalidOperationException
{
    public SeasonNotActiveException(DateTimeOffset occurredAtUtc)
        : base($"No accepted season is active at {occurredAtUtc:O}.")
        => OccurredAtUtc = occurredAtUtc;

    public DateTimeOffset OccurredAtUtc { get; }
}

public sealed class SeasonProgressionClosedException : InvalidOperationException
{
    public SeasonProgressionClosedException(string seasonId, int seasonVersion)
        : base($"Season '{seasonId}' version {seasonVersion} is closed for XP grants.")
    {
        SeasonId = seasonId;
        SeasonVersion = seasonVersion;
    }

    public string SeasonId { get; }
    public int SeasonVersion { get; }
}

public sealed class SeasonProgressionService
{
    public const int MaxGrantIdLength = 128;
    public const int MaxReasonLength = 128;

    private readonly ISeasonProgressionRepository _repository;
    private readonly ISeasonRepository _seasons;
    private readonly ProgressionDefinitionSnapshot _definitions;

    public SeasonProgressionService(
        ISeasonProgressionRepository repository,
        ISeasonRepository seasons,
        ProgressionDefinitionSnapshot definitions)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _seasons = seasons ?? throw new ArgumentNullException(nameof(seasons));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    }

    public async ValueTask<SeasonProgressionResult> ReadAsync(
        PlayerId playerId,
        string seasonId,
        int seasonVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!SeasonCatalogSnapshot.ValidIdentifier(seasonId) || seasonVersion < 1)
            throw new ArgumentException("Season key is invalid.", nameof(seasonId));

        var state = await _repository.ReadAsync(
            playerId, seasonId, seasonVersion, cancellationToken).ConfigureAwait(false);
        return new SeasonProgressionResult(
            state.PlayerId,
            state.SeasonId,
            state.SeasonVersion,
            state.SeasonXp,
            state.Revision,
            _definitions.LevelFor(state.SeasonXp));
    }

    public async ValueTask<SeasonProgressionResult?> ReadCurrentAsync(
        PlayerId playerId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        var instant = NormalizeUtc(at);
        var accepted = await _seasons.ListEffectiveAsync(cancellationToken).ConfigureAwait(false);
        var season = accepted.FirstOrDefault(value =>
            value.Definition.StartsAtUtc <= instant
            && instant < value.Definition.EndsAtUtc);
        if (season is null)
            return null;

        return await ReadAsync(
            playerId,
            season.Definition.Id,
            season.Definition.Version,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SeasonXpGrantResult> GrantAsync(
        PlayerId playerId,
        SeasonXpGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var occurredAtUtc = NormalizeUtc(request.OccurredAt);
        var accepted = await _seasons.ListEffectiveAsync(cancellationToken).ConfigureAwait(false);
        var season = accepted.FirstOrDefault(value =>
            value.Definition.StartsAtUtc <= occurredAtUtc
            && occurredAtUtc < value.Definition.EndsAtUtc)
            ?? throw new SeasonNotActiveException(occurredAtUtc);

        var existing = await _repository.ReadGrantAsync(
            playerId,
            season.Definition.Id,
            request.GrantId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!MatchesOriginalRequest(existing, request, occurredAtUtc)
                || existing.SeasonVersion != season.Definition.Version)
            {
                throw new SeasonXpGrantConflictException(
                    playerId, season.Definition.Id, request.GrantId);
            }

            return new SeasonXpGrantResult(
                false,
                existing,
                _definitions.LevelFor(existing.SeasonXpAfter));
        }

        if (season.ClosedAtUtc is not null)
            throw new SeasonProgressionClosedException(
                season.Definition.Id, season.Definition.Version);

        var boost = request.Source == ProgressionXpSource.Administration
            ? new XpBoostResolution(null, 1m)
            : _definitions.ResolveBoost(occurredAtUtc, request.Source);
        var awardedXp = request.Source == ProgressionXpSource.Administration
            ? request.BaseXp
            : _definitions.ApplyBoost(request.BaseXp, occurredAtUtc, request.Source);

        var committed = await _repository.ApplyAsync(
            playerId,
            new SeasonXpGrantCandidate(
                season.Definition.Id,
                season.Definition.Version,
                request.GrantId,
                request.Source,
                request.BaseXp,
                awardedXp,
                request.Reason,
                occurredAtUtc,
                boost.BoostId,
                boost.Multiplier),
            cancellationToken).ConfigureAwait(false);

        return new SeasonXpGrantResult(
            committed.Applied,
            committed.Grant,
            _definitions.LevelFor(committed.Grant.SeasonXpAfter));
    }

    private static void ValidateRequest(SeasonXpGrantRequest request)
    {
        if (!PrintableBounded(request.GrantId, MaxGrantIdLength)
            || request.GrantId.Any(character => character > 0x7f)
            || !PrintableBounded(request.Reason, MaxReasonLength)
            || !Enum.IsDefined(request.Source)
            || request.Source != ProgressionXpSource.Administration && request.BaseXp < 0)
        {
            throw new ArgumentException("Season XP grant request is invalid.", nameof(request));
        }
    }

    private static bool MatchesOriginalRequest(
        SeasonXpGrantRecord existing,
        SeasonXpGrantRequest request,
        DateTimeOffset occurredAtUtc)
        => existing.Source == request.Source
            && existing.BaseXp == request.BaseXp
            && string.Equals(existing.Reason, request.Reason, StringComparison.Ordinal)
            && existing.OccurredAtUtc == occurredAtUtc;

    private static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }
}
