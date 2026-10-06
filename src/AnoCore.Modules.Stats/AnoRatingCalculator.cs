using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public enum AnoRatingConfidence { Provisional, Low, Medium, High }

public sealed record AnoRatingDimension(string Name, double Value, double Weight);

public sealed record AnoRatingResult(string AlgorithmVersion, int? Score,
    AnoRatingConfidence Confidence, long Rounds, CombatTotals Combat,
    IReadOnlyList<AnoRatingDimension> Dimensions);

public static class AnoRatingCalculator
{
    public const string Version = "ano-rating-v1";

    public static AnoRatingResult Calculate(CombatTotals combat, CombatDetailTotals? details,
        IReadOnlyList<GameplayStatTotal> gameplay)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(gameplay);
        if (combat.Kills < 0 || combat.Deaths < 0 || combat.Assists < 0
            || gameplay.Any(total => total is null || total.Count < 0 || !Enum.IsDefined(total.Kind))
            || details is not null && (details.Shots < 0 || details.Hits < 0
                || details.DamageHealth < 0 || details.DamageArmor < 0 || details.HeadHits < 0))
            throw new ArgumentOutOfRangeException(nameof(combat), "Rating inputs must contain valid nonnegative counts.");
        long Count(GameplayStatKind kind)
            => gameplay.Where(total => total.Kind == kind)
                .Aggregate(0L, (sum, total) => checked(sum + total.Count));
        var rounds = Count(GameplayStatKind.RoundPlayed);
        var observations = (double)combat.Kills + combat.Deaths;
        var dimensions = new List<AnoRatingDimension>();
        if (observations > 0 || combat.Assists > 0)
        {
            var ratio = (combat.Kills + 0.5 * combat.Assists + 10) / (combat.Deaths + 10.0);
            dimensions.Add(new("combat", 100 * ratio / (1 + ratio), 0.40));
        }
        if (rounds > 0)
        {
            if (details is { Shots: > 0 } || details is { Hits: > 0 })
                dimensions.Add(new("impact", Math.Clamp(details.DamageHealth / (double)rounds, 0, 100), 0.25));
            var objectives = (double)Count(GameplayStatKind.BombPlanted)
                + Count(GameplayStatKind.BombDefused) + Count(GameplayStatKind.HostageRescued);
            dimensions.Add(new("objective", Math.Clamp(100 * objectives / rounds, 0, 100), 0.10));
            dimensions.Add(new("utility", Math.Clamp(100.0 * Count(GameplayStatKind.FlashAssist) / rounds, 0, 100), 0.10));
            var wins = Count(GameplayStatKind.RoundWon);
            var losses = Count(GameplayStatKind.RoundLost);
            var outcomes = (double)wins + losses;
            if (outcomes > 0 && outcomes <= rounds)
                dimensions.Add(new("consistency", 100 * wins / outcomes, 0.15));
        }
        var confidence = rounds < 20 || observations < 20
            ? AnoRatingConfidence.Provisional
            : rounds >= 500 && observations >= 500 && dimensions.Count == 5
                && details is { Shots: >= 500 }
                ? AnoRatingConfidence.High
                : rounds >= 100 && observations >= 100 && dimensions.Count >= 4
                    ? AnoRatingConfidence.Medium : AnoRatingConfidence.Low;
        int? score = confidence == AnoRatingConfidence.Provisional ? null
            : (int)(10 * Math.Round(dimensions.Sum(dimension => dimension.Value * dimension.Weight)
                / dimensions.Sum(dimension => dimension.Weight), MidpointRounding.AwayFromZero));
        return new(Version, score, confidence, rounds, combat, dimensions.AsReadOnly());
    }
}
