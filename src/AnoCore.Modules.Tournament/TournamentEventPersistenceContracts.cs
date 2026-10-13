namespace AnoCore.Modules.Tournament;

/// <summary>An atomic, versioned view of one complete event for read-only servers.</summary>
public sealed record TournamentStoredEvent(
    TournamentEventDefinition Definition,
    IReadOnlyList<TournamentGroupResult> ConfirmedResults,
    long Revision)
{
    public IReadOnlyList<TournamentBracketResult> PlayoffResults { get; init; } = [];
}

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
    ValueTask<TournamentStoredEvent> SavePlayoffsAsync(string eventId, long expectedRevision,
        IReadOnlyCollection<TournamentBracketResult> results, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This repository does not support playoff persistence.");
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
            TournamentSchedules.Generate(definition, groupId,
                results.Where(result => result is not null && result.FixtureId is not null && result.FixtureId.StartsWith(groupId + ":", StringComparison.Ordinal)).ToArray()).SelectMany(round => round.Fixtures)
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

/// <summary>Shared stable seed projection used by commands, persistence and UI.</summary>
public static class TournamentEventPlayoffs
{
    public static IReadOnlyList<TournamentBracketEntrant>? ResolveEntrants(TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> groupResults)
    {
        TournamentEventSnapshotValidator.Validate(definition, groupResults);
        if (!definition.PlayoffsEnabled) return null;
        if (definition.PlayoffQualifiersPerGroup is not { } count) return Entrants(definition);
        var groups = definition.Groups.Count == 0 ? new[] { "main" }
            : definition.Groups.OrderBy(group => group.Id, StringComparer.Ordinal).Select(group => group.Id).ToArray();
        if (groups.Any(group => !TournamentSchedules.IsComplete(definition, group,
            groupResults.Where(result => result.FixtureId.StartsWith(group + ":", StringComparison.Ordinal)).ToArray()))) return null;
        var ranked = groups.Select(group => TournamentStandings.Calculate(definition, group,
            groupResults.Where(result => result.FixtureId.StartsWith(group + ":", StringComparison.Ordinal)).ToArray())).ToArray();
        var qualified = Enumerable.Range(0, count).SelectMany(position => ranked.Select(rows => rows[position].TeamId));
        var entrants = qualified.Select((id, index) => new TournamentBracketEntrant(id, index + 1)).ToArray();
        if (!definition.CrossGroupOpeningPairings) return entrants;
        var teamGroups = definition.Groups.SelectMany(group => group.TeamIds.Select(id => (id, group.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);
        return TournamentGroupCrossing.Arrange(entrants, teamGroups);
    }

    public static TournamentBracketProgressSnapshot? Progress(TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> groups, IReadOnlyCollection<TournamentBracketResult> playoffs)
    {
        ArgumentNullException.ThrowIfNull(playoffs);
        var entrants = ResolveEntrants(definition, groups);
        if (entrants is null)
        {
            if (playoffs.Count > 0) throw new ArgumentException("Playoffs are disabled or the qualifying group stage is incomplete.");
            return null;
        }
        if (definition.PlayoffBestOf is { } bestOf && playoffs.Any(result => result is null
            || result.WinnerMaps != bestOf / 2 + 1 || result.LoserMaps >= bestOf / 2 + 1))
            throw new ArgumentException("Playoff result does not complete the configured BestOf series.", nameof(playoffs));
        return definition.PlayoffFormat == TournamentPlayoffFormat.DoubleElimination
            ? TournamentDoubleElimination.Calculate(entrants, playoffs, definition.GrandFinalReset)
            : definition.ReseedPlayoffRounds
                ? TournamentReseededSingleElimination.Calculate(entrants, playoffs).Progress
                : TournamentBracketProgression.Calculate(entrants, playoffs);
    }

    public static IReadOnlyList<TournamentBracketEntrant> Entrants(TournamentEventDefinition definition)
    {
        TournamentEventDefinition.RequireValid(definition);
        return definition.Teams.OrderBy(team => team.InitialSeed ?? int.MaxValue)
            .ThenBy(team => team.Id, StringComparer.Ordinal)
            .Select((team, index) => new TournamentBracketEntrant(team.Id, index + 1)).ToArray();
    }
}

public static class TournamentPlayoffPlans
{
    public static TournamentBracketPlan Generate(TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentBracketEntrant> entrants,
        IReadOnlyCollection<TournamentBracketResult>? confirmedResults = null)
        => definition.PlayoffFormat == TournamentPlayoffFormat.DoubleElimination
            ? TournamentDoubleElimination.Generate(entrants, definition.GrandFinalReset)
            : definition.ReseedPlayoffRounds
                ? TournamentReseededSingleElimination.Calculate(entrants, confirmedResults ?? []).Plan
                : TournamentSingleElimination.GenerateFinalStage(entrants);
}
