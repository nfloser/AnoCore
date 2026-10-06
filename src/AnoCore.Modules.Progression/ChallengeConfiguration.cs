using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record RecurringChallengeTemplate(string Id, int Version, string Name, ChallengeWindowKind WindowKind,
    GameplayStatKind Statistic, long Target, long RewardXp, IReadOnlyList<string> PrerequisiteIds);

public sealed class ChallengeConfiguration
{
    public bool Enabled { get; set; } = true;
    public int CheckpointSeconds { get; set; } = 30;
    public List<RecurringChallengeTemplate> Recurring { get; set; } =
    [
        new("weekly.headshots", 1, "Weekly headshots", ChallengeWindowKind.Weekly, GameplayStatKind.HeadshotKill, 10, 100, []),
        new("weekly.round-wins", 1, "Weekly round wins", ChallengeWindowKind.Weekly, GameplayStatKind.RoundWon, 10, 100, []),
        new("weekly.bomb-plants", 1, "Weekly bomb plants", ChallengeWindowKind.Weekly, GameplayStatKind.BombPlanted, 5, 100, []),
    ];
    public List<ChallengeDefinition> Predefined { get; set; } = [];

    public ChallengeScheduleSnapshot Snapshot()
        => ChallengeScheduleSnapshot.Create(this);

    public static IReadOnlyCollection<string> Validate(ChallengeConfiguration configuration)
    {
        try { ArgumentNullException.ThrowIfNull(configuration); _ = configuration.Snapshot(); return []; }
        catch (ArgumentException exception) { return [exception.Message]; }
        catch (InvalidOperationException exception) { return [exception.Message]; }
    }
}

public sealed class ChallengeScheduleSnapshot
{
    private readonly IReadOnlyList<RecurringChallengeTemplate> _recurring;
    private readonly IReadOnlyList<ChallengeDefinition> _predefined;

    private ChallengeScheduleSnapshot(int checkpointSeconds, IReadOnlyList<RecurringChallengeTemplate> recurring,
        IReadOnlyList<ChallengeDefinition> predefined)
    {
        CheckpointSeconds = checkpointSeconds;
        _recurring = recurring;
        _predefined = predefined;
    }

    public int CheckpointSeconds { get; }

    internal static ChallengeScheduleSnapshot Create(ChallengeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.CheckpointSeconds is < 10 or > 600 || configuration.Recurring is null
            || configuration.Predefined is null || configuration.Recurring.Count > 32 || configuration.Predefined.Count > 96)
            throw new ArgumentException("Challenges require a 10-600 second checkpoint, at most 32 recurring and 96 predefined definitions.");
        var recurring = configuration.Recurring.Select(item =>
        {
            if (item is null || item.PrerequisiteIds is null)
                throw new ArgumentException("Recurring challenges and prerequisites cannot be null.");
            var prerequisites = item.PrerequisiteIds.Take(ChallengeCatalogSnapshot.MaxPrerequisites + 1).ToArray();
            if (prerequisites.Length > ChallengeCatalogSnapshot.MaxPrerequisites)
                throw new ArgumentException("Recurring prerequisites exceed the supported bound.");
            return item with
            {
                PrerequisiteIds = Array.AsReadOnly(prerequisites),
            };
        }).ToArray();
        if (recurring.Any(item => item.WindowKind is not ChallengeWindowKind.Daily and not ChallengeWindowKind.Weekly))
            throw new ArgumentException("Recurring challenges must be daily or weekly; seasons require dated predefined definitions.");
        var predefined = ChallengeCatalogSnapshot.Create(configuration.Predefined).Challenges;
        var result = new ChallengeScheduleSnapshot(configuration.CheckpointSeconds, Array.AsReadOnly(recurring), predefined);
        var catalog = result.ResolveAt(new DateTimeOffset(2027, 1, 4, 0, 0, 0, TimeSpan.Zero));
        if (catalog.Challenges.Any(item => item.StartsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0
            || item.EndsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0))
            throw new ArgumentException("Challenge windows require microsecond precision.");
        return result;
    }

    public ChallengeCatalogSnapshot ResolveAt(DateTimeOffset at)
    {
        var day = new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero);
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var definitions = new List<ChallengeDefinition>(_predefined);
        foreach (var item in _recurring)
        {
            var start = item.WindowKind == ChallengeWindowKind.Daily ? day : monday;
            var end = start.AddDays(item.WindowKind == ChallengeWindowKind.Daily ? 1 : 7);
            definitions.Add(new(item.Id, item.Version, item.Name, item.WindowKind, item.Statistic,
                item.Target, item.RewardXp, start, end, item.PrerequisiteIds));
        }
        return ChallengeCatalogSnapshot.Create(definitions);
    }
}
