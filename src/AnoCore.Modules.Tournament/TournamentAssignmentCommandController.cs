using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;

namespace AnoCore.Modules.Tournament;

public sealed class TournamentMatchZyReplayConfiguration
{
    public ulong AssignmentId { get; set; }
    public List<string> Payloads { get; set; } = [];
    public static IReadOnlyCollection<string> Validate(TournamentMatchZyReplayConfiguration? value)
        => value is null || value.AssignmentId is 0 or > long.MaxValue || value.Payloads is null || value.Payloads.Count is < 1 or > 6
            || value.Payloads.Any(payload => payload is null || System.Text.Encoding.UTF8.GetByteCount(payload) > TournamentMatchZy.MaximumPayloadBytes)
            ? ["Replay requires a positive assignment ID and 1-6 bounded original event JSON payloads."] : [];
}

/// <summary>Audited recovery operations; no arbitrary file paths, console text or remote URLs are accepted.</summary>
public sealed class TournamentAssignmentCommandController : IDisposable
{
    private readonly List<IDisposable> _registrations = [];
    public TournamentAssignmentCommandController(IAnoCommandRegistry registry, ITournamentMatchAssignmentRepository repository,
        TournamentMatchZyDispatcher dispatcher, IConfigStore config, IAdminAuditService audit)
    {
        var owner = new ModuleId("ano.tournament-assignments");
        var manage = new PermissionId("ano.tournament.manage");
        _registrations.Add(registry.Register(owner, new("anotournamentassignments", "Show pending/dispatched tournament assignments."), async context =>
        {
            var open = await repository.LoadOpenAsync(context.CancellationToken).ConfigureAwait(false);
            return CommandResult.Ok(open.Count == 0 ? "[ANO] No open MatchZy assignments." : "[ANO] " + string.Join(" | ", open.Select(match =>
                $"#{match.Id} {match.ServerId}: {match.HomeTeamId} vs {match.AwayTeamId} [{match.State}]")));
        }));
        _registrations.Add(registry.Register(owner, new("anotournamentassignmentcancel", "Cancel this server's assignment and end MatchZy.", manage,
            arguments: [new("id", CommandArgumentKind.UInt64, "Numeric assignment id.")]), async context =>
        {
            var id = context.Get<ulong>("id");
            if (id is 0 or > long.MaxValue) return CommandResult.Fail(CommandFailureReason.InvalidInput, "Invalid assignment ID.");
            await audit.RecordAsync(new AdminActionId("tournament.assignment-cancel.requested"), context.Caller, null, id.ToString(), DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            await dispatcher.CancelAsync((long)id, context.CancellationToken).ConfigureAwait(false);
            await audit.RecordAsync(new AdminActionId("tournament.assignment-cancel"), context.Caller, null, id.ToString(), DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            return CommandResult.Ok("[ANO] Assignment canceled; MatchZy end requested. Result callbacks for this assignment are rejected.");
        }));
        _registrations.Add(registry.Register(owner, new("anotournamentmatchzyreplay", "Replay fixed tournament-matchzy-replay JSON after failed HTTP delivery.", manage), async context =>
        {
            var replay = await config.LoadAsync("tournament-matchzy-replay", () => new TournamentMatchZyReplayConfiguration(),
                TournamentMatchZyReplayConfiguration.Validate, context.CancellationToken).ConfigureAwait(false);
            await audit.RecordAsync(new AdminActionId("tournament.matchzy-replay.requested"), context.Caller, null, replay.AssignmentId.ToString(), DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            var accepted = 0;
            try
            {
                foreach (var payload in replay.Payloads)
                {
                    await repository.ApplyEventAsync((long)replay.AssignmentId, payload, context.CancellationToken).ConfigureAwait(false);
                    accepted++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return CommandResult.Fail(CommandFailureReason.HandlerFailed, $"Accepted {accepted} events before replay stopped: {exception.Message}. Repeating accepted events is safe."); }
            await audit.RecordAsync(new AdminActionId("tournament.matchzy-replay"), context.Caller, null, replay.AssignmentId.ToString(), DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            return CommandResult.Ok($"[ANO] Replayed {accepted} MatchZy events; duplicate results do not advance the event twice.");
        }));
    }
    public void Dispose() { foreach (var registration in _registrations) registration.Dispose(); _registrations.Clear(); }
}
