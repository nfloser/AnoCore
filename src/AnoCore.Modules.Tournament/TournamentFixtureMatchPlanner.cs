using System.Security.Cryptography;
using System.Text;

namespace AnoCore.Modules.Tournament;

/// <summary>
/// A proposed, not yet claimed/started, CS2 5v5 match backed by one event fixture.
/// The caller must acquire a server-scoped DB lease before starting it.
/// </summary>
public sealed record TournamentReadyMatch(
    string Stage,
    string SourceId,
    string HomeTeamId,
    string AwayTeamId,
    TournamentMatchDefinition Match);

/// <summary>
/// Translates resolved group/playoff pairings into validated standalone
/// TournamentMatchDefinition objects. Does not perform any live match write.
/// </summary>
public static class TournamentFixtureMatchPlanner
{
    public static IReadOnlyList<TournamentReadyMatch> Plan(
        TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> confirmedGroups,
        IReadOnlyCollection<TournamentBracketResult> confirmedPlayoffs,
        int defaultGroupBestOf = 1, int defaultPlayoffBestOf = 3)
    {
        if (defaultGroupBestOf is not (1 or 3 or 5)
            || defaultPlayoffBestOf is not (1 or 3 or 5))
            throw new ArgumentOutOfRangeException(nameof(defaultGroupBestOf),
                "Default group and playoff BestOf must be 1, 3 or 5.");
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(confirmedGroups);
        ArgumentNullException.ThrowIfNull(confirmedPlayoffs);
        TournamentEventSnapshotValidator.Validate(definition, confirmedGroups);

        var progress = TournamentEventPlayoffs.Progress(definition, confirmedGroups, confirmedPlayoffs);
        var teams = definition.Teams.ToDictionary(team => team.Id, StringComparer.Ordinal);
        var ready = new List<TournamentReadyMatch>();

        if (definition.GroupStageEnabled)
        {
            var groupIds = definition.Groups.Count == 0
                ? ["main"]
                : definition.Groups.OrderBy(group => group.Id, StringComparer.Ordinal)
                    .Select(group => group.Id).ToArray();
            foreach (var groupId in groupIds)
            {
                var prefix = groupId + ":";
                var completed = confirmedGroups.Where(result =>
                    result.FixtureId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                var alreadyPlayed = completed.Select(result => result.FixtureId)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var fixture in TournamentSchedules.Generate(definition, groupId, completed)
                    .SelectMany(round => round.Fixtures)
                    .Where(fixture => !alreadyPlayed.Contains(fixture.Id)))
                {
                    ready.Add(Create(definition.Id, "group", fixture.Id,
                        fixture.HomeTeamId, fixture.AwayTeamId, teams,
                        definition.GroupBestOf ?? defaultGroupBestOf));
                }
            }
        }

        // Even when a playoff uses the original seeds rather than group
        // qualification, starting it while the same teams are playing the
        // group stage is unsafe. A match scheduler must respect stage order.
        var groupsComplete = !definition.GroupStageEnabled
            || (definition.Groups.Count == 0 ? ["main"]
                : definition.Groups.Select(group => group.Id).ToArray()).All(groupId =>
                    TournamentSchedules.IsComplete(definition, groupId, confirmedGroups.Where(result =>
                        result.FixtureId.StartsWith(groupId + ":", StringComparison.Ordinal)).ToArray()));
        if (definition.PlayoffsEnabled && groupsComplete && progress is not null)
        {
            foreach (var match in progress.Matches.Where(match => match.IsReady))
            {
                ready.Add(Create(definition.Id, "playoff", match.Id,
                    match.HomeTeamId!, match.AwayTeamId!, teams,
                    definition.PlayoffBestOf ?? defaultPlayoffBestOf));
            }
        }

        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        if (ready.Any(item => !uniqueIds.Add(item.Match.MatchId)))
            throw new InvalidOperationException("The event fixture plan produced colliding match IDs.");
        return ready.AsReadOnly();
    }

    private static TournamentReadyMatch Create(
        string eventId, string stage, string fixtureId, string homeId,
        string awayId, IReadOnlyDictionary<string, TournamentEventTeamDefinition> teams, int bestOf)
    {
        if (homeId == awayId)
            throw new InvalidOperationException("A tournament fixture cannot play a team against itself.");
        if (!teams.TryGetValue(homeId, out var home) || !teams.TryGetValue(awayId, out var away))
            throw new InvalidOperationException("Tournament fixture references an unregistered team.");
        var match = new TournamentMatchDefinition
        {
            Enabled = true,
            MatchId = MatchId(eventId, stage, fixtureId).ToString("D"),
            BestOf = bestOf,
            KnifeRound = true,
            OvertimeEnabled = true,
            TeamA = ToRoster(home),
            TeamB = ToRoster(away),
        };
        var errors = TournamentMatchDefinition.Validate(match);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(teams));
        return new TournamentReadyMatch(stage, fixtureId, homeId, awayId, match);
    }

    private static TournamentTeamDefinition ToRoster(TournamentEventTeamDefinition team)
        => new()
        {
            Name = team.Name,
            Tag = team.Tag,
            CaptainSteamId = team.CaptainSteamId,
            Members = [.. team.Starters],
        };

    private static Guid MatchId(string eventId, string stage, string fixtureId)
    {
        var input = Encoding.UTF8.GetBytes($"anocore:tournament-fixture:v1\0{eventId}\0{stage}\0{fixtureId}");
        Span<byte> guid = stackalloc byte[16];
        SHA256.HashData(input).AsSpan(0, 16).CopyTo(guid);
        // Apply UUID version/variant bits for a valid non-random, name-based ID.
        guid[7] = (byte)((guid[7] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }
}
