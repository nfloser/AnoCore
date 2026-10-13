namespace AnoCore.Modules.Tournament;

public sealed class TournamentMatchZyConfiguration
{
    public bool Enabled { get; set; }
    public string ResultBaseUrl { get; set; } = "";
    public List<string> Maps { get; set; } = ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis"];
    public int DefaultGroupBestOf { get; set; } = 1;
    public int DefaultPlayoffBestOf { get; set; } = 3;
    public static IReadOnlyCollection<string> Validate(TournamentMatchZyConfiguration? value)
    {
        if (value is null) return ["MatchZy configuration is required."];
        if (!value.Enabled) return [];
        var errors = new List<string>();
        if (!Uri.TryCreate(value.ResultBaseUrl, UriKind.Absolute, out var url)
            || (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback))
            || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            errors.Add("ResultBaseUrl must use HTTPS (or loopback HTTP) without credentials, query or fragment.");
        if (value.DefaultGroupBestOf is not (1 or 3 or 5) || value.DefaultPlayoffBestOf is not (1 or 3 or 5))
            errors.Add("Default BestOf must be 1, 3 or 5.");
        if (value.Maps is null || value.Maps.Count is < 5 or > 16 || value.Maps.Any(map => !TournamentMatchZy.ValidMap(map))
            || value.Maps.Distinct(StringComparer.Ordinal).Count() != value.Maps.Count)
            errors.Add("Configure 5-16 distinct safe map names.");
        return errors;
    }
}

public sealed record TournamentAssignableFixture(string Id, bool IsPlayoff, string HomeTeamId, string AwayTeamId, int BestOf);
public static class TournamentMatchAssignmentPlan
{
    public static IReadOnlyList<TournamentAssignableFixture> Ready(TournamentStoredEvent stored, TournamentMatchZyConfiguration config)
    {
        var definition = stored.Definition;
        TournamentEventSnapshotValidator.Validate(definition, stored.ConfirmedResults);
        var groups = definition.Groups.Count == 0 ? new[] { "main" } : definition.Groups.Select(group => group.Id).ToArray();
        var pending = groups.SelectMany(group => TournamentSchedules.Generate(definition, group,
                stored.ConfirmedResults.Where(result => result.FixtureId.StartsWith(group + ":", StringComparison.Ordinal)).ToArray()))
            .SelectMany(round => round.Fixtures).Where(fixture => !stored.ConfirmedResults.Any(result => result.FixtureId == fixture.Id))
            .Select(fixture => new TournamentAssignableFixture(fixture.Id, false, fixture.HomeTeamId, fixture.AwayTeamId,
                definition.GroupBestOf ?? config.DefaultGroupBestOf)).ToArray();
        if (pending.Length > 0) return pending;
        var progress = TournamentEventPlayoffs.Progress(definition, stored.ConfirmedResults, stored.PlayoffResults);
        return progress?.Matches.Where(match => match.IsReady).Select(match => new TournamentAssignableFixture(match.Id,
            true, match.HomeTeamId!, match.AwayTeamId!, definition.PlayoffBestOf ?? config.DefaultPlayoffBestOf)).ToArray() ?? [];
    }
}

public interface ITournamentMatchAssignmentRepository
{
    ValueTask<TournamentMatchClaim?> ClaimNextAsync(string serverId, Guid instanceId, TournamentMatchZyConfiguration configuration,
        CancellationToken cancellationToken = default);
    ValueTask<bool> MarkDispatchedAsync(long assignmentId, string serverId, Guid instanceId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TournamentMatchAssignment>> LoadOpenAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> AuthenticateAsync(long assignmentId, string token, CancellationToken cancellationToken = default);
    ValueTask<bool> ApplyEventAsync(long assignmentId, string payload, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(long assignmentId, string serverId, Guid instanceId, CancellationToken cancellationToken = default);
}
