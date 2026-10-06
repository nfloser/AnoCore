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

    private readonly IReadOnlyList<SeasonDefinition> _seasons;

    private SeasonCatalogSnapshot(IReadOnlyList<SeasonDefinition> seasons)
        => _seasons = seasons;

    public IReadOnlyList<SeasonDefinition> Seasons => _seasons;

    public static SeasonCatalogSnapshot Create(
        IEnumerable<SeasonDefinition> seasons)
    {
        ArgumentNullException.ThrowIfNull(seasons);

        var snapshot = seasons.ToArray();
        if (snapshot.Length > MaxSeasons)
            throw new ArgumentException(
                $"Define at most {MaxSeasons} seasons in one catalog.",
                nameof(seasons));

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var season in snapshot)
        {
            if (season is null)
                throw new ArgumentException(
                    "Season catalogs cannot contain null definitions.",
                    nameof(seasons));
            if (!ValidIdentifier(season.Id))
                throw new ArgumentException(
                    "Season IDs must be 1-64 printable ASCII identifier characters.",
                    nameof(seasons));
            if (!identifiers.Add(season.Id))
                throw new ArgumentException(
                    "A season catalog can contain only one effective version per season ID.",
                    nameof(seasons));
            if (season.Version < 1)
                throw new ArgumentException(
                    "Season definition versions must be positive.",
                    nameof(seasons));
            if (!PrintableBounded(season.Name, MaxSeasonNameLength))
                throw new ArgumentException(
                    "Season names must contain 1-128 printable characters.",
                    nameof(seasons));
            if (season.StartsAtUtc.Offset != TimeSpan.Zero
                || season.EndsAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Season windows must be defined in UTC.",
                    nameof(seasons));
            }
            if (season.StartsAtUtc >= season.EndsAtUtc)
                throw new ArgumentException(
                    "Season windows use [start, end) semantics and require start before end.",
                    nameof(seasons));
        }

        var ordered = snapshot
            .OrderBy(season => season.StartsAtUtc)
            .ThenBy(season => season.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (current.StartsAtUtc < previous.EndsAtUtc)
                throw new SeasonOverlapException(current.Id, previous.Id);
        }

        return new SeasonCatalogSnapshot(
            ordered.ToList().AsReadOnly());
    }

    public SeasonCatalogResolution ResolveAt(DateTimeOffset at)
    {
        var instant = at.ToUniversalTime();
        SeasonDefinition? current = null;
        SeasonDefinition? previous = null;
        SeasonDefinition? next = null;

        foreach (var season in _seasons)
        {
            if (season.EndsAtUtc <= instant)
            {
                previous = season;
                continue;
            }

            if (season.StartsAtUtc <= instant)
            {
                current = season;
                continue;
            }

            next = season;
            break;
        }

        return new SeasonCatalogResolution(current, previous, next);
    }

    internal static bool ValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaxSeasonIdLength
            && value == value.Trim()
            && value.All(character =>
                character <= 0x7f
                && (char.IsLetterOrDigit(character)
                    || character is '.' or '_' or '-'));

    internal static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));
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
