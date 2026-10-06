using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public enum ChallengeWindowKind
{
    Daily,
    Weekly,
    Season,
}

public enum ChallengeEvaluationState
{
    Future,
    Locked,
    Active,
    ReadyToComplete,
    Completed,
    Expired,
}

public sealed record ChallengeDefinition(
    string Id,
    int Version,
    string Name,
    ChallengeWindowKind WindowKind,
    GameplayStatKind Statistic,
    long Target,
    long RewardXp,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    IReadOnlyList<string> PrerequisiteIds);

public sealed record ChallengeEvaluation(
    ChallengeDefinition Definition,
    ChallengeEvaluationState State,
    long Progress,
    long Target,
    bool CompletionCandidate,
    IReadOnlyList<string> MissingPrerequisites);

public sealed class ChallengeCatalogSnapshot
{
    public const int MaxChallenges = 512;
    public const int MaxPrerequisites = 32;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 128;

    private readonly IReadOnlyList<ChallengeDefinition> _challenges;
    private readonly IReadOnlyDictionary<string, ChallengeDefinition> _byId;

    private ChallengeCatalogSnapshot(
        IReadOnlyList<ChallengeDefinition> challenges,
        IReadOnlyDictionary<string, ChallengeDefinition> byId)
    {
        _challenges = challenges;
        _byId = byId;
    }

    public IReadOnlyList<ChallengeDefinition> Challenges => _challenges;

    public static ChallengeCatalogSnapshot Create(
        IEnumerable<ChallengeDefinition> challenges)
    {
        ArgumentNullException.ThrowIfNull(challenges);
        var snapshot = challenges.Take(MaxChallenges + 1).ToArray();
        if (snapshot.Length > MaxChallenges)
            throw new ArgumentException(
                $"Define at most {MaxChallenges} challenges.", nameof(challenges));

        var normalized = new List<ChallengeDefinition>(snapshot.Length);
        var byId = new Dictionary<string, ChallengeDefinition>(StringComparer.Ordinal);
        foreach (var challenge in snapshot)
        {
            if (challenge is null)
                throw new ArgumentException(
                    "Challenge catalogs cannot contain null definitions.",
                    nameof(challenges));

            Validate(challenge);
            if (!byId.TryAdd(challenge.Id, challenge))
                throw new ArgumentException(
                    $"Challenge ID '{challenge.Id}' is duplicated.",
                    nameof(challenges));

            var prerequisites = challenge.PrerequisiteIds
                .ToArray();
            var copy = challenge with
            {
                PrerequisiteIds = Array.AsReadOnly(prerequisites),
            };
            normalized.Add(copy);
            byId[copy.Id] = copy;
        }

        ValidateDependencies(normalized, byId);

        var ordered = normalized
            .OrderBy(value => value.StartsAtUtc)
            .ThenBy(value => value.WindowKind)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();

        return new ChallengeCatalogSnapshot(
            Array.AsReadOnly(ordered),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, ChallengeDefinition>(
                byId));
    }

    public ChallengeDefinition Get(string challengeId)
    {
        ValidateId(challengeId, nameof(challengeId));
        return _byId.TryGetValue(challengeId, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Challenge '{challengeId}' is not in the catalog.");
    }

    public ChallengeEvaluation Evaluate(
        string challengeId,
        IEnumerable<GameplayStatTotal> windowTotals,
        IEnumerable<string>? completedChallengeIds,
        DateTimeOffset at)
        => Evaluate(
            Get(challengeId),
            windowTotals,
            completedChallengeIds,
            at);

    private ChallengeEvaluation Evaluate(
        ChallengeDefinition definition,
        IEnumerable<GameplayStatTotal> windowTotals,
        IEnumerable<string>? completedChallengeIds,
        DateTimeOffset at)
    {
        var totals = ValidateTotals(windowTotals);
        var completed = ValidateCompleted(completedChallengeIds);
        return Evaluate(definition, totals, completed, NormalizeUtc(at));
    }

    private static ChallengeEvaluation Evaluate(
        ChallengeDefinition definition,
        IReadOnlyDictionary<GameplayStatKind, long> totals,
        IReadOnlySet<string> completed,
        DateTimeOffset instant)
    {
        var progress = totals.TryGetValue(definition.Statistic, out var value)
            ? value
            : 0;
        var cappedProgress = Math.Min(progress, definition.Target);

        if (completed.Contains(definition.Id))
            return Result(ChallengeEvaluationState.Completed, false, []);

        if (instant < definition.StartsAtUtc)
            return Result(ChallengeEvaluationState.Future, false, []);

        if (instant >= definition.EndsAtUtc)
            return Result(ChallengeEvaluationState.Expired, false, []);

        var missing = definition.PrerequisiteIds
            .Where(id => !completed.Contains(id))
            .ToArray();
        if (missing.Length > 0)
            return Result(ChallengeEvaluationState.Locked, false, missing);

        return progress >= definition.Target
            ? Result(ChallengeEvaluationState.ReadyToComplete, true, [])
            : Result(ChallengeEvaluationState.Active, false, []);

        ChallengeEvaluation Result(
            ChallengeEvaluationState state,
            bool candidate,
            IReadOnlyList<string> missing)
            => new(
                definition,
                state,
                cappedProgress,
                definition.Target,
                candidate,
                missing);
    }

    private static void Validate(ChallengeDefinition challenge)
    {
        ValidateId(challenge.Id, nameof(challenge.Id));
        if (challenge.Version < 1)
            throw new ArgumentException(
                "Challenge versions must be positive.", nameof(challenge));
        if (!PrintableBounded(challenge.Name, MaxNameLength))
            throw new ArgumentException(
                "Challenge names must contain 1-128 printable characters.",
                nameof(challenge));
        if (!Enum.IsDefined(challenge.WindowKind)
            || !Enum.IsDefined(challenge.Statistic)
            || challenge.Target < 1
            || challenge.RewardXp < 0)
        {
            throw new ArgumentException(
                "Challenge kind, statistic, target or reward is invalid.",
                nameof(challenge));
        }
        if (challenge.StartsAtUtc.Offset != TimeSpan.Zero
            || challenge.EndsAtUtc.Offset != TimeSpan.Zero
            || challenge.StartsAtUtc >= challenge.EndsAtUtc)
        {
            throw new ArgumentException(
                "Challenge windows must be non-empty UTC half-open ranges.",
                nameof(challenge));
        }

        var duration = challenge.EndsAtUtc - challenge.StartsAtUtc;
        if (challenge.WindowKind == ChallengeWindowKind.Daily
            && duration != TimeSpan.FromDays(1)
            || challenge.WindowKind == ChallengeWindowKind.Weekly
            && duration != TimeSpan.FromDays(7))
        {
            throw new ArgumentException(
                "Daily challenges require 24-hour windows and weekly challenges require 7-day windows.",
                nameof(challenge));
        }

        if (challenge.PrerequisiteIds is null)
            throw new ArgumentException(
                "Challenge prerequisites cannot be null.", nameof(challenge));

        if (challenge.PrerequisiteIds.Count > MaxPrerequisites)
            throw new ArgumentException(
                $"A challenge can have at most {MaxPrerequisites} prerequisites.",
                nameof(challenge));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prerequisite in challenge.PrerequisiteIds)
        {
            ValidateId(prerequisite, nameof(challenge));
            if (!seen.Add(prerequisite)
                || string.Equals(prerequisite, challenge.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Challenge prerequisites must be unique and cannot reference the challenge itself.",
                    nameof(challenge));
            }
        }
    }

    private static void ValidateDependencies(
        IReadOnlyList<ChallengeDefinition> challenges,
        IReadOnlyDictionary<string, ChallengeDefinition> byId)
    {
        foreach (var challenge in challenges)
        {
            foreach (var prerequisite in challenge.PrerequisiteIds)
            {
                if (!byId.ContainsKey(prerequisite))
                    throw new ArgumentException(
                        $"Challenge '{challenge.Id}' references missing prerequisite '{prerequisite}'.",
                        nameof(challenges));
            }
        }

        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var challenge in challenges)
            Visit(challenge);

        void Visit(ChallengeDefinition challenge)
        {
            if (state.TryGetValue(challenge.Id, out var current))
            {
                if (current == 1)
                    throw new ArgumentException(
                        $"Challenge dependency cycle includes '{challenge.Id}'.",
                        nameof(challenges));
                if (current == 2)
                    return;
            }

            state[challenge.Id] = 1;
            foreach (var prerequisite in challenge.PrerequisiteIds)
                Visit(byId[prerequisite]);
            state[challenge.Id] = 2;
        }
    }

    private static IReadOnlyDictionary<GameplayStatKind, long> ValidateTotals(
        IEnumerable<GameplayStatTotal> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        var result = new Dictionary<GameplayStatKind, long>();
        var limit = Enum.GetValues<GameplayStatKind>().Length;
        foreach (var total in totals.Take(limit + 1))
        {
            if (total is null
                || !Enum.IsDefined(total.Kind)
                || total.Count < 0
                || !result.TryAdd(total.Kind, total.Count))
            {
                throw new ArgumentException(
                    "Challenge totals require unique known statistics with nonnegative counts.",
                    nameof(totals));
            }
        }

        if (result.Count > limit)
            throw new ArgumentException(
                "Challenge totals exceed the supported statistic set.", nameof(totals));
        return result;
    }

    private IReadOnlySet<string> ValidateCompleted(
        IEnumerable<string>? completedChallengeIds)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (completedChallengeIds is null)
            return completed;

        foreach (var id in completedChallengeIds.Take(MaxChallenges + 1))
        {
            ValidateId(id, nameof(completedChallengeIds));
            if (!_byId.ContainsKey(id) || !completed.Add(id))
                throw new ArgumentException(
                    "Completed challenge IDs must be unique catalog members.",
                    nameof(completedChallengeIds));
        }
        if (completed.Count > MaxChallenges)
            throw new ArgumentException(
                "Completed challenge IDs exceed the catalog bound.",
                nameof(completedChallengeIds));
        return completed;
    }

    private static void ValidateId(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaxIdLength
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Challenge IDs require 1-64 ASCII letters, digits, dots, dashes or underscores.",
                paramName);
        }
    }

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
