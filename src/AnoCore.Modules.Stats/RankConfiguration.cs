using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed record RankThreshold(string Name, long MinimumPoints);

public sealed class RankConfiguration
{
    public int KillPoints { get; set; } = 2;
    public int AssistPoints { get; set; } = 1;
    public int DeathPenalty { get; set; } = 1;
    public bool NotifyRankChanges { get; set; } = true;
    public bool NotifyAdministrativeRankChanges { get; set; } = true;
    public List<RankThreshold> Thresholds { get; set; } =
    [
        new("Recruit", 0),
        new("Veteran", 10),
        new("Elite", 100),
    ];

    public static RankConfiguration Default => new();

    public long Score(CombatTotals totals) => Math.Max(0, RawScore(totals));

    public long RawScore(CombatTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (totals.Kills < 0 || totals.Deaths < 0 || totals.Assists < 0)
            throw new ArgumentOutOfRangeException(nameof(totals));
        return checked(checked(totals.Kills * KillPoints)
            + checked(totals.Assists * AssistPoints)
            - checked(totals.Deaths * DeathPenalty));
    }

    public RankThreshold ForScore(long points)
        => Thresholds.Last(threshold => points >= threshold.MinimumPoints);

    public RankThreshold? NextAfter(long points)
    {
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        return Thresholds.FirstOrDefault(threshold => threshold.MinimumPoints > points);
    }

    public static IReadOnlyCollection<string> Validate(RankConfiguration configuration)
    {
        if (configuration is null) return ["Rank configuration is required."];
        var errors = new List<string>();
        if (configuration.KillPoints is < 1 or > 1000
            || configuration.AssistPoints is < 0 or > 1000
            || configuration.DeathPenalty is < 0 or > 1000)
            errors.Add("Rank weights must be between 0 and 1000; kills must award points.");
        if (configuration.Thresholds is null || configuration.Thresholds.Count is < 1 or > 100)
        {
            errors.Add("Define between 1 and 100 rank thresholds.");
            return errors;
        }

        long previous = -1;
        foreach (var threshold in configuration.Thresholds)
        {
            if (threshold is null || string.IsNullOrWhiteSpace(threshold.Name)
                || threshold.Name.Length > 48
                || threshold.Name.Any(char.IsControl))
                errors.Add("Rank names must contain 1 to 48 printable characters.");
            if (threshold is null || threshold.MinimumPoints <= previous
                || threshold.MinimumPoints < 0)
                errors.Add("Rank thresholds must be strictly increasing and start at zero.");
            if (threshold is not null) previous = threshold.MinimumPoints;
        }
        if (configuration.Thresholds[0]?.MinimumPoints != 0)
            errors.Add("The first rank threshold must start at zero.");
        return errors;
    }
}
