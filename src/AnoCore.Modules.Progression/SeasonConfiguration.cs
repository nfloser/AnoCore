namespace AnoCore.Modules.Progression;

public sealed class SeasonConfiguration
{
    public bool Enabled { get; set; }
    public int CheckpointSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
    public List<SeasonDefinition> Seasons { get; set; } = [];

    public SeasonConfigurationSnapshot Snapshot()
    {
        if (CheckpointSeconds is < 10 or > 600 || BatchSize is < 1 or > 100 || Seasons is null)
            throw new ArgumentException("Seasons require bounded checkpoints/batches and a catalog.");
        return new(CheckpointSeconds, BatchSize, SeasonCatalogSnapshot.Create(Seasons));
    }

    public static IReadOnlyList<string> Validate(SeasonConfiguration configuration)
    {
        if (configuration is null) return ["Season configuration is required."];
        try { _ = configuration.Snapshot(); return []; }
        catch (ArgumentException exception) { return [exception.Message]; }
    }
}

public sealed record SeasonConfigurationSnapshot(int CheckpointSeconds, int BatchSize, SeasonCatalogSnapshot Catalog);
