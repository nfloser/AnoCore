using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public enum StatisticsCategory
{
    Playtime = 1,
    Kills = 2,
    Deaths = 3,
    Assists = 4,
    KillDeathRatio = 5,
    HeadshotPercentage = 6,
    MatchWins = 7,
    MatchWinPercentage = 8,
    RoundWins = 9,
    Mvp = 10,
    BombPlants = 11,
    BombDefuses = 12,
    GrenadeKills = 13,
    KnifeKills = 14,
}

public sealed record StatisticsRankEntry(PlayerId PlayerId, decimal Value, int Position, string? DisplayName);

/// <summary>Headshots and native kills share the same persisted native-context cohort.</summary>
public sealed record PersonalStatistics(CombatTotals Combat, TimeSpan Playtime,
    long Headshots, long NativeKills, long MatchWins, long MatchLosses, long RoundWins, long Mvp);

/// <summary>Additive optional capability; existing statistics repositories remain valid.</summary>
public interface IStatisticsMenuRepository
{
    ValueTask<PersonalStatistics> ReadPersonalAsync(PlayerId player, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<StatisticsRankEntry>> GetTopAsync(StatisticsCategory category,
        int limit, int offset, CancellationToken cancellationToken = default);
}
