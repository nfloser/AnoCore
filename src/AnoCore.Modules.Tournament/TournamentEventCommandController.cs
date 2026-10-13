using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;

namespace AnoCore.Modules.Tournament;

/// <summary>Audited operator entry points for the event-wide tournament plan.</summary>
public sealed class TournamentEventCommandController : IDisposable
{
    public const string DefinitionConfigName = "tournament-event";
    private static readonly ModuleId Owner = new("ano.tournament-events");
    private static readonly PermissionId Manage = new("ano.tournament.manage");
    private readonly IConfigStore _config;
    private readonly ITournamentEventRepository _repository;
    private readonly IAdminAuditService _audit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public TournamentEventCommandController(IConfigStore config, IAnoCommandRegistry registry,
        ITournamentEventRepository repository, IAdminAuditService audit)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        ArgumentNullException.ThrowIfNull(registry);

        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventload", "Import the fixed tournament-event JSON and activate it.", Manage),
            ImportAsync));
        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventbracket", "Show playoff pairings and champion."), BracketAsync));
        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventplayoffresult", "Confirm one playoff winner.", Manage, arguments:
                [new("match", CommandArgumentKind.String, "Stable playoff match id."),
                 new("winner", CommandArgumentKind.String, "Winning team id."),
                 new("winner_maps", CommandArgumentKind.Int32, "Maps won by the winner."),
                 new("loser_maps", CommandArgumentKind.Int32, "Maps won by the loser.")]), ConfirmPlayoffAsync));
        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventstatus", "Show current shared tournament event status."), StatusAsync));
        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventstandings", "Show group standings.", arguments:
                [new("group", CommandArgumentKind.String, "Group id.", required: false)]), StandingsAsync));
        _registrations.Add(registry.Register(Owner,
            new("anotournamenteventresult", "Confirm one scheduled group fixture result.", Manage,
                arguments:
                [
                    new("fixture", CommandArgumentKind.String, "Stable generated fixture id."),
                    new("home_maps", CommandArgumentKind.Int32, "Home maps won."),
                    new("away_maps", CommandArgumentKind.Int32, "Away maps won."),
                    new("home_rounds", CommandArgumentKind.Int32, "Total rounds won by home."),
                    new("away_rounds", CommandArgumentKind.Int32, "Total rounds won by away."),
                ]), ConfirmAsync));
    }

    private async ValueTask<CommandResult> BracketAsync(CommandContext context)
    {
        ThrowIfDisposed();
        var active = await _repository.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
        if (active is null) return CommandResult.Ok("[ANO] No active shared tournament.");
        var progress = TournamentEventPlayoffs.Progress(active.Definition, active.ConfirmedResults, active.PlayoffResults);
        if (progress is null) return CommandResult.Ok(active.Definition.PlayoffsEnabled
            ? "[ANO] Playoffs wait for all group fixtures to be confirmed." : "[ANO] Playoffs are disabled for this event.");
        return CommandResult.Ok("[ANO] " + string.Join(" | ", progress.Matches.Where(match => match.IsEnabled).Select(match =>
            $"{match.Id}: {match.HomeTeamId ?? "pending"} vs {match.AwayTeamId ?? "pending"}" +
            (match.WinnerTeamId is not null ? $" -> {match.WinnerTeamId}" : match.IsReady ? " [ready]" : " [waiting]")))
            + (progress.ChampionTeamId is null ? "" : $" | Champion: {progress.ChampionTeamId}"));
    }

    private async ValueTask<CommandResult> ConfirmPlayoffAsync(CommandContext context)
    {
        await _gate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var active = await _repository.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
            if (active is null) return CommandResult.Fail(CommandFailureReason.InvalidInput, "No active shared tournament.");
            var result = new TournamentBracketResult(context.Get<string>("match"), context.Get<string>("winner"),
                context.Get<int>("winner_maps"), context.Get<int>("loser_maps"));
            var results = active.PlayoffResults.Append(result).ToArray();
            try
            {
                if (TournamentEventPlayoffs.Progress(active.Definition, active.ConfirmedResults, results) is null)
                    return CommandResult.Fail(CommandFailureReason.InvalidInput, "Group stage is incomplete.");
            }
            catch (ArgumentException exception) { return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message); }
            if (!await AuditAsync("tournament.playoff-result.requested", context, result.MatchId))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed, "Playoff result could not be audited.");
            try
            {
                await _repository.SavePlayoffsAsync(active.Definition.Id, active.Revision, results, context.CancellationToken).ConfigureAwait(false);
            }
            catch (TournamentEventConcurrencyException)
            {
                return CommandResult.Fail(CommandFailureReason.HandlerFailed, "Event changed on another server. Reload and retry.");
            }
            if (!await AuditAsync("tournament.playoff-result", context, result.MatchId))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed, "Result saved; completion audit failed. Do not submit again.");
            return CommandResult.Ok($"[ANO] Playoff winner recorded: {result.MatchId} -> {result.WinnerTeamId}.");
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<CommandResult> ImportAsync(CommandContext context)
    {
        await _gate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            TournamentEventImportConfiguration document;
            try
            {
                document = await _config.LoadAsync(
                    DefinitionConfigName,
                    () => new TournamentEventImportConfiguration(),
                    TournamentEventImportConfiguration.Validate,
                    context.CancellationToken).ConfigureAwait(false);
                if (!document.Enabled)
                    return CommandResult.Fail(CommandFailureReason.InvalidInput,
                        "Edit config/tournament-event.json, set Enabled=true and retry.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput,
                    $"Tournament import rejected: {exception.Message}");
            }

            var definition = document.Event;
            if (!await AuditAsync("tournament.event-import.requested", context, definition.Id))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Tournament import request could not be audited.");
            TournamentStoredEvent? old;
            try
            {
                old = await _repository.LoadAsync(definition.Id, context.CancellationToken).ConfigureAwait(false);
                if (old is null)
                    await _repository.CreateAsync(definition, context.CancellationToken).ConfigureAwait(false);
                else
                    await _repository.SaveAsync(definition.Id, old.Revision, definition,
                        old.ConfirmedResults, context.CancellationToken).ConfigureAwait(false);
                await _repository.ActivateAsync(definition.Id, context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    $"Tournament import was not fully activated: {exception.Message}");
            }

            if (!await AuditAsync("tournament.event-import", context, definition.Id))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Tournament import committed, but completion audit failed. Check tournament status.");
            return CommandResult.Ok($"[ANO] Tournament '{definition.Name}' ({definition.Teams.Count} teams) activated.");
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<CommandResult> StatusAsync(CommandContext context)
    {
        ThrowIfDisposed();
        var active = await _repository.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
        if (active is null) return CommandResult.Ok("[ANO] No shared tournament event is active.");
        return CommandResult.Ok($"[ANO] {active.Definition.Name}: {active.Definition.Teams.Count} teams; " +
            $"{active.ConfirmedResults.Count} confirmed matches; revision {active.Revision}.");
    }

    private async ValueTask<CommandResult> StandingsAsync(CommandContext context)
    {
        ThrowIfDisposed();
        var active = await _repository.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
        if (active is null) return CommandResult.Ok("[ANO] No shared tournament event is active.");
        var group = context.TryGet<string>("group", out var requested) && !string.IsNullOrWhiteSpace(requested)
            ? requested : active.Definition.Groups.FirstOrDefault()?.Id ?? "main";
        IReadOnlyList<TournamentTeamStanding> rows;
        try
        {
            var prefix = group + ":";
            rows = TournamentStandings.Calculate(active.Definition, group,
                active.ConfirmedResults.Where(result =>
                    result.FixtureId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (ArgumentException e)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, e.Message);
        }
        var names = active.Definition.Teams.ToDictionary(t => t.Id, t => t.Name, StringComparer.OrdinalIgnoreCase);
        var text = string.Join(" | ", rows.Select((row, index) =>
            $"{index + 1}. {names[row.TeamId]} {row.Points}pts ({row.Wins}-{row.Draws}-{row.Losses}) " +
            $"maps {row.MapDifference:+#;-#;0} rounds {row.RoundDifference:+#;-#;0}"));
        return CommandResult.Ok($"[ANO] Group {group} | {text}");
    }

    private async ValueTask<CommandResult> ConfirmAsync(CommandContext context)
    {
        await _gate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var active = await _repository.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
            if (active is null)
                return CommandResult.Fail(CommandFailureReason.InvalidInput, "No active shared tournament.");
            var result = new TournamentGroupResult(
                context.Get<string>("fixture"),
                context.Get<int>("home_maps"),
                context.Get<int>("away_maps"),
                context.Get<int>("home_rounds"),
                context.Get<int>("away_rounds"));
            if (active.ConfirmedResults.Any(existing => existing.FixtureId == result.FixtureId))
                return CommandResult.Fail(CommandFailureReason.InvalidInput,
                    "This fixture already has a confirmed result; correction requires a separate audited operation.");
            if (!await AuditAsync("tournament.result.requested", context, result.FixtureId))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Result request could not be audited.");
            try
            {
                await _repository.SaveAsync(active.Definition.Id, active.Revision,
                    active.Definition, active.ConfirmedResults.Append(result).ToArray(),
                    context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TournamentEventConcurrencyException)
            {
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Event changed on another server. Reload and retry; result was not committed.");
            }
            catch (ArgumentException e)
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput, e.Message);
            }
            if (!await AuditAsync("tournament.result", context, result.FixtureId))
                return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Result saved, completion audit failed. Do not submit again.");
            return CommandResult.Ok($"[ANO] Tournament result recorded: {result.FixtureId}.");
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<bool> AuditAsync(string name, CommandContext context, string detail)
    {
        try
        {
            await _audit.RecordAsync(new AdminActionId(name), context.Caller, targetId: null,
                detail, DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var registration in _registrations) registration.Dispose();
    }
}
