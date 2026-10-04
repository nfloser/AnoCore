namespace AnoCore.Modules.Stats;

public sealed class GameplayStatsConfiguration
{
    public bool WarmupStats { get; set; }
    public int MinimumPlayers { get; set; } = 4;
    public bool FreeForAll { get; set; }

    public static GameplayStatsConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(GameplayStatsConfiguration configuration)
    {
        if (configuration is null) return ["Gameplay statistics configuration is required."];
        if (configuration.MinimumPlayers is < 1 or > 64)
            return ["MinimumPlayers must be between 1 and 64."];
        return [];
    }
}

public static class GameplayStatsEligibility
{
    public static bool IsAllowed(GameplayStatsConfiguration configuration,
        bool isWarmup, int trackedHumanPlayers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (GameplayStatsConfiguration.Validate(configuration).Count != 0)
            throw new ArgumentException("Gameplay statistics configuration is invalid.",
                nameof(configuration));
        if (trackedHumanPlayers < 0)
            throw new ArgumentOutOfRangeException(nameof(trackedHumanPlayers));
        return (!isWarmup || configuration.WarmupStats)
            && trackedHumanPlayers >= configuration.MinimumPlayers;
    }
}
