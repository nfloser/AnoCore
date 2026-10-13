namespace AnoCore.Modules.Tournament;

/// <summary>An atomic, versioned view of one complete event for read-only servers.</summary>
public sealed record TournamentStoredEvent(
    TournamentEventDefinition Definition,
    IReadOnlyList<TournamentGroupResult> ConfirmedResults,
    long Revision);

public interface ITournamentEventRepository
{
    ValueTask<TournamentStoredEvent> CreateAsync(
        TournamentEventDefinition definition, CancellationToken cancellationToken = default);
    ValueTask<TournamentStoredEvent?> LoadAsync(
        string eventId, CancellationToken cancellationToken = default);
    ValueTask<TournamentStoredEvent?> LoadActiveAsync(
        CancellationToken cancellationToken = default);
    ValueTask<TournamentStoredEvent> SaveAsync(
        string eventId, long expectedRevision,
        TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> confirmedResults,
        CancellationToken cancellationToken = default);
    ValueTask ActivateAsync(string eventId, CancellationToken cancellationToken = default);
}

/// <summary>Raised instead of allowing concurrent server writes to silently overwrite one another.</summary>
public sealed class TournamentEventConcurrencyException : InvalidOperationException
{
    public TournamentEventConcurrencyException(string eventId, long expectedRevision)
        : base($"Tournament '{eventId}' was changed by another server; expected revision {expectedRevision}. Reload and retry.")
    {
        EventId = eventId;
        ExpectedRevision = expectedRevision;
    }

    public string EventId { get; }
    public long ExpectedRevision { get; }
}

/// <summary>Validation for the fully published version, not a mutable in-progress editor.</summary>
public static class TournamentEventSnapshotValidator
{
    public static void Validate(TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> results)
    {
        TournamentEventDefinition.RequireValid(definition);
        ArgumentNullException.ThrowIfNull(results);
        var groups = definition.Groups.Count == 0
            ? new[] { "main" }
            : definition.Groups.Select(group => group.Id).ToArray();
        var known = groups.SelectMany(groupId =>
            TournamentRoundRobin.Generate(definition, groupId).SelectMany(round => round.Fixtures)
                .Select(fixture => (fixture.Id, GroupId: groupId)))
            .ToDictionary(x => x.Id, x => x.GroupId, StringComparer.Ordinal);
        var grouped = groups.ToDictionary(id => id, _ => new List<TournamentGroupResult>(),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result is null || !seen.Add(result.FixtureId)
                || !known.TryGetValue(result.FixtureId, out var groupId))
                throw new ArgumentException("An event result references an unknown or duplicated fixture.", nameof(results));
            grouped[groupId].Add(result);
        }
        foreach (var group in groups)
            _ = TournamentStandings.Calculate(definition, group, grouped[group]);
    }
}
