using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record AchievementCatalogEntry(string Id, int Version, string Name,
    GameplayStatKind Statistic, List<AchievementTier> Tiers);

public sealed record NamedAchievement(string Name, AchievementDefinition Definition);

public sealed record AchievementCatalogSnapshot(int CheckpointSeconds,
    ProgressionDefinitionSnapshot Xp, IReadOnlyList<NamedAchievement> Achievements);

public sealed class AchievementConfiguration
{
    public bool Enabled { get; set; } = true;
    public int CheckpointSeconds { get; set; } = 30;
    public List<XpLevelThreshold> Levels { get; set; } =
        [new(1, 0), new(2, 100), new(3, 300), new(4, 600), new(5, 1000)];
    public List<XpBoostDefinition> Boosts { get; set; } = [];
    public List<AchievementCatalogEntry> Achievements { get; set; } =
    [
        new("headshots", 1, "Headshots", GameplayStatKind.HeadshotKill,
            [new(1, 10, 100), new(2, 50, 200), new(3, 100, 300)]),
        new("round-wins", 1, "Round wins", GameplayStatKind.RoundWon,
            [new(1, 10, 100), new(2, 50, 200), new(3, 100, 300)]),
        new("bomb-plants", 1, "Bomb plants", GameplayStatKind.BombPlanted,
            [new(1, 5, 100), new(2, 25, 200), new(3, 50, 300)]),
    ];

    public AchievementCatalogSnapshot Snapshot()
    {
        if (CheckpointSeconds is < 10 or > 600)
            throw new ArgumentException("Achievement checkpoints must be between 10 and 600 seconds.");
        if (Achievements is null || Achievements.Count is < 1 or > 32)
            throw new ArgumentException("Define between 1 and 32 achievements.");
        if (Levels is null || Boosts is null)
            throw new ArgumentException("XP levels and boosts cannot be null.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new List<NamedAchievement>();
        foreach (var entry in Achievements)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 48
                || entry.Name != entry.Name.Trim() || entry.Name.Any(char.IsControl) || !ids.Add(entry.Id))
                throw new ArgumentException("Achievement IDs must be unique and names printable with at most 48 characters.");
            snapshot.Add(new NamedAchievement(entry.Name,
                AchievementDefinition.Create(entry.Id, entry.Version, entry.Statistic, entry.Tiers)));
        }
        return new AchievementCatalogSnapshot(CheckpointSeconds,
            ProgressionDefinitionSnapshot.Create(Levels, Boosts),
            snapshot.OrderBy(entry => entry.Definition.Id, StringComparer.Ordinal).ToList().AsReadOnly());
    }

    public static IReadOnlyCollection<string> Validate(AchievementConfiguration configuration)
    {
        if (configuration is null) return ["Achievement configuration is required."];
        try
        {
            _ = configuration.Snapshot();
            return [];
        }
        catch (ArgumentException exception)
        {
            return [exception.Message];
        }
    }
}
