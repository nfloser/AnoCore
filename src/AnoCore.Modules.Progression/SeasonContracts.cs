namespace AnoCore.Modules.Progression;

public sealed record SeasonDefinition(
    string Id,
    int Version,
    string Name,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

public sealed record SeasonCatalogResolution(
    SeasonDefinition? Current,
    SeasonDefinition? Previous,
    SeasonDefinition? Next);

public sealed class SeasonCatalogSnapshot
{
    public const int MaxSeasons = 128;
    public const int MaxSeasonIdLength = 64;
    public const int MaxSeasonNameLength = 128;

    public IReadOnlyList<SeasonDefinition> Seasons
        => throw new NotImplementedException();

    public static SeasonCatalogSnapshot Create(
        IEnumerable<SeasonDefinition> seasons)
        => throw new NotImplementedException();

    public SeasonCatalogResolution ResolveAt(DateTimeOffset at)
        => throw new NotImplementedException();
}

public sealed record PersistedSeason(
    SeasonDefinition Definition,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record SeasonAcceptResult(
    bool Inserted,
    PersistedSeason Season);

public interface ISeasonRepository
{
    ValueTask<IReadOnlyList<PersistedSeason>> ListEffectiveAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PersistedSeason?> ReadAsync(
        string seasonId,
        int version,
        CancellationToken cancellationToken = default);

    ValueTask<SeasonAcceptResult> AcceptAsync(
        SeasonDefinition definition,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<PersistedSeason> CloseAsync(
        string seasonId,
        int version,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class SeasonDefinitionConflictException : InvalidOperationException
{
    public SeasonDefinitionConflictException(string seasonId, int version)
        : base($"Season '{seasonId}' version {version} conflicts with an accepted definition.")
    {
        SeasonId = seasonId;
        Version = version;
    }

    public string SeasonId { get; }
    public int Version { get; }
}

public sealed class SeasonOverlapException : InvalidOperationException
{
    public SeasonOverlapException(string seasonId, string conflictingSeasonId)
        : base($"Season '{seasonId}' overlaps accepted season '{conflictingSeasonId}'.")
    {
        SeasonId = seasonId;
        ConflictingSeasonId = conflictingSeasonId;
    }

    public string SeasonId { get; }
    public string ConflictingSeasonId { get; }
}
