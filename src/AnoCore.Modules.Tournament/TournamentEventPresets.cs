using System.Text.Json;

namespace AnoCore.Modules.Tournament;

public enum TournamentEventPreset { RoundRobin, Groups, Swiss, SingleElimination, DoubleElimination }
public sealed record TournamentEventPreview(int Teams, IReadOnlyList<int> GroupSizes, int GroupMatches, int PlayoffEntrants,
    int GroupByes, int OpeningByes, int MinimumMatches, int MaximumMatches);

public static class TournamentEventPresets
{
    public static TournamentEventDefinition Apply(TournamentEventDefinition source, TournamentEventPreset preset)
    {
        TournamentEventDefinition.RequireValid(source);
        if (!Enum.IsDefined(preset)) throw new ArgumentException("Unknown tournament preset.", nameof(preset));
        var definition = JsonSerializer.Deserialize<TournamentEventDefinition>(JsonSerializer.Serialize(source))!;
        definition.Groups = [];
        definition.GroupStageEnabled = preset is TournamentEventPreset.RoundRobin or TournamentEventPreset.Groups or TournamentEventPreset.Swiss;
        definition.PlayoffsEnabled = preset is TournamentEventPreset.Groups or TournamentEventPreset.SingleElimination or TournamentEventPreset.DoubleElimination;
        definition.GroupFormat = preset == TournamentEventPreset.Swiss ? TournamentGroupFormat.Swiss : TournamentGroupFormat.RoundRobin;
        definition.PlayoffFormat = preset == TournamentEventPreset.DoubleElimination ? TournamentPlayoffFormat.DoubleElimination : TournamentPlayoffFormat.SingleElimination;
        definition.GroupBestOf = 1; definition.PlayoffBestOf = 3; definition.SwissRounds = null; definition.GrandFinalReset = true;
        definition.PlayoffQualifiersPerGroup = null;
        definition.CrossGroupOpeningPairings = false;
        if (preset == TournamentEventPreset.Groups)
        {
            var ids = TournamentEventPlayoffs.Entrants(definition).Select(entry => entry.TeamId).ToArray();
            var groups = ids.Length < 4 ? 1 : 2;
            definition.Groups = Enumerable.Range(0, groups).Select(index => new TournamentGroupDefinition
            { Id = $"group-{index + 1}", Name = $"Group {index + 1}", TeamIds = ids.Where((_, i) => i % groups == index).ToList() }).ToList();
            definition.PlayoffQualifiersPerGroup = 2;
            definition.CrossGroupOpeningPairings = groups >= 2;
        }
        if (preset == TournamentEventPreset.Swiss) definition.Standings.Tiebreakers = [TournamentTiebreaker.Buchholz, TournamentTiebreaker.MapDifference, TournamentTiebreaker.InitialSeed];
        TournamentEventDefinition.RequireValid(definition);
        return definition;
    }
    public static TournamentEventPreview Preview(TournamentEventDefinition definition)
    {
        TournamentEventDefinition.RequireValid(definition);
        var sizes = !definition.GroupStageEnabled ? [] : definition.Groups.Count == 0 ? new[] { definition.Teams.Count }
            : definition.Groups.Select(group => group.TeamIds.Count).ToArray();
        var groupMatches = sizes.Sum(size => definition.GroupFormat == TournamentGroupFormat.Swiss
            ? TournamentSchedules.SwissRoundCount(definition, size) * (size / 2) : size * (size - 1) / 2);
        var groupByes = sizes.Sum(size => size % 2 == 0 ? 0 : definition.GroupFormat == TournamentGroupFormat.Swiss
            ? TournamentSchedules.SwissRoundCount(definition, size) : size);
        var entrants = !definition.PlayoffsEnabled ? 0 : definition.PlayoffQualifiersPerGroup is { } qualifiers
            ? qualifiers * sizes.Length : definition.Teams.Count;
        var slots = entrants == 0 ? 0 : 2; while (slots < entrants) slots *= 2;
        var playoffs = entrants == 0 ? 0 : definition.PlayoffFormat == TournamentPlayoffFormat.DoubleElimination ? 2 * entrants - 2 : entrants - 1;
        return new(definition.Teams.Count, sizes, groupMatches, entrants, groupByes, slots - entrants, groupMatches + playoffs,
            groupMatches + playoffs + (entrants > 0 && definition.PlayoffFormat == TournamentPlayoffFormat.DoubleElimination && definition.GrandFinalReset ? 1 : 0));
    }
}
