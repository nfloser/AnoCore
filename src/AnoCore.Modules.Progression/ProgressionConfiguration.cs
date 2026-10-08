using AnoCore.Abstractions.Configuration;

namespace AnoCore.Modules.Progression;

/// <summary>Shared lifetime curve and scheduled boosts, independent of achievement activation.</summary>
public sealed class ProgressionConfiguration
{
    public List<XpLevelThreshold> Levels { get; set; } =
        [new(1, 0), new(2, 100), new(3, 300), new(4, 600), new(5, 1000)];
    public List<XpBoostDefinition> Boosts { get; set; } = [];

    public ProgressionDefinitionSnapshot Snapshot()
        => ProgressionDefinitionSnapshot.Create(Levels ?? throw new ArgumentException("progression.Levels cannot be null."),
            Boosts ?? throw new ArgumentException("progression.Boosts cannot be null."));

    public static IReadOnlyCollection<string> Validate(ProgressionConfiguration value)
    {
        try { ArgumentNullException.ThrowIfNull(value); _ = value.Snapshot(); return []; }
        catch (ArgumentException exception) { return [exception.Message]; }
    }

    public static async ValueTask<ProgressionDefinitionSnapshot> LoadAsync(IConfigStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            // A missing-file factory is deferred by IConfigStore. Do not create defaults before
            // the legacy policy is read, or replace an invalid existing canonical file.
            var existing = await store.LoadAsync<ProgressionConfiguration>("progression",
                () => throw new MissingSharedPolicyException(), Validate, cancellationToken).ConfigureAwait(false);
            return existing.Snapshot();
        }
        catch (MissingSharedPolicyException)
        {
            var legacy = await store.LoadAsync("achievements", () => new AchievementConfiguration(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var migrated = new ProgressionConfiguration { Levels = legacy.Levels, Boosts = legacy.Boosts };
            var snapshot = migrated.Snapshot();
            var accepted = await store.LoadAsync("progression", () => new ProgressionConfiguration
            {
                Levels = snapshot.Levels.ToList(),
                Boosts = snapshot.Boosts.ToList(),
            }, Validate, cancellationToken).ConfigureAwait(false);
            return accepted.Snapshot();
        }
    }

    private sealed class MissingSharedPolicyException : Exception;
}
