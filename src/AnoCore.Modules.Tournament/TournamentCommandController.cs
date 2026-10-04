using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public sealed class TournamentCommandController : IDisposable
{
    public const string DefinitionConfigName = "tournament-match";

    private static readonly ModuleId Owner = new("ano.tournament");
    private static readonly PermissionId ManagePermission = new("ano.tournament.manage");

    private readonly IConfigStore _configuration;
    private readonly IPlayerRegistry _players;
    private readonly TournamentRecoveryService _recovery;
    private readonly TournamentMatchRuntime _runtime;
    private readonly IAdminAuditService _audit;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly List<IDisposable> _registrations = [];
    private int _disposed;

    public TournamentCommandController(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        TournamentRecoveryService recovery,
        TournamentMatchRuntime runtime,
        IAdminAuditService audit,
        TimeProvider? time = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? TimeProvider.System;

        try
        {
            Register(commands);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static PermissionId GetManagePermission() => ManagePermission;

    private void Register(IAnoCommandRegistry commands)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentload",
                "Load and activate the validated tournament-match configuration.",
                ManagePermission),
            LoadAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentstatus",
                "Show the active tournament match state."),
            StatusAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentreadyopen",
                "Open ready-up for the active tournament match.",
                ManagePermission),
            context => MutateAdminAsync(
                context,
                "ready-open",
                machine =>
                {
                    machine.OpenReady();
                    return MutationResult.Save("[ANO] Tournament ready-up opened.");
                })));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anoready",
                "Mark your current tournament roster slot ready."),
            ReadyAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentmaps",
                "Commit the resolved tournament map series.",
                ManagePermission,
                arguments:
                [new("maps", CommandArgumentKind.String,
                    "Comma-separated map series in play order.")]),
            CommitMapsAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentknife",
                "Record the knife-round winner.",
                ManagePermission,
                arguments:
                [new("team", CommandArgumentKind.String, "Team a or b.")]),
            KnifeAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentside",
                "Choose T or CT after winning the knife round.",
                arguments:
                [new("side", CommandArgumentKind.String, "t or ct.")]),
            SideAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentpause",
                "Pause the active tournament match.",
                ManagePermission),
            context => MutateAdminAsync(
                context,
                "pause",
                machine =>
                {
                    machine.Pause();
                    return MutationResult.Save("[ANO] Tournament paused.");
                })));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentresume",
                "Resume the active tournament match.",
                ManagePermission),
            context => MutateAdminAsync(
                context,
                "resume",
                machine =>
                {
                    machine.Resume();
                    return MutationResult.Save("[ANO] Tournament resumed.");
                })));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentovertime",
                "Enter tournament overtime.",
                ManagePermission),
            context => MutateAdminAsync(
                context,
                "overtime",
                machine =>
                {
                    machine.EnterOvertime();
                    return MutationResult.Save("[ANO] Tournament overtime started.");
                })));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentmapwin",
                "Record the winning team for the current map.",
                ManagePermission,
                arguments:
                [new("team", CommandArgumentKind.String, "Team a or b.")]),
            MapWinAsync));

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotournamentabandon",
                "Deactivate the current tournament match without deleting its snapshot.",
                ManagePermission,
                arguments:
                [new("reason", CommandArgumentKind.String,
                    "Optional audit reason.", required: false)]),
            AbandonAsync));
    }

    private async ValueTask<CommandResult> LoadAsync(CommandContext context)
    {
        await _mutation.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            TournamentMatchDefinition definition;
            try
            {
                definition = await _configuration.LoadAsync(
                    DefinitionConfigName,
                    () => TournamentMatchDefinition.Default,
                    TournamentMatchDefinition.Validate,
                    context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    $"Tournament definition could not be loaded: {exception.Message}");
            }

            TournamentMatchConfiguration match;
            try
            {
                match = definition.ToConfiguration();
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
            }

            var reason = $"match={match.MatchId:D}; bestof={(int)match.BestOf}";
            if (!await AuditRequestedAsync(
                    "load", context.Caller, reason, context.CancellationToken)
                    .ConfigureAwait(false))
            {
                return AuditRequestFailed();
            }

            TournamentRecoverySession session;
            try
            {
                session = await _recovery.BeginAsync(
                    match, makeActive: true, context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CommandResult.Fail(
                    CommandFailureReason.HandlerFailed,
                    "The tournament definition was audited but could not be persisted.");
            }

            _runtime.Replace(session);
            if (!await AuditCompletedAsync(
                    "load", context.Caller, reason, context.CancellationToken)
                    .ConfigureAwait(false))
            {
                return PersistedButAuditFailed();
            }

            return CommandResult.Ok(
                $"[ANO] Tournament {match.MatchId:D} loaded (BO{(int)match.BestOf}).");
        }
        finally
        {
            _mutation.Release();
        }
    }

    private ValueTask<CommandResult> StatusAsync(CommandContext context)
    {
        ThrowIfDisposed();
        var session = _runtime.CurrentSession;
        if (session is null)
            return ValueTask.FromResult(CommandResult.Ok("[ANO] No active tournament match."));

        var machine = session.Machine;
        var map = machine.Maps.Count == 0
            ? "none"
            : machine.Maps[Math.Min(machine.CurrentMapIndex, machine.Maps.Count - 1)];
        var readyA = machine.Configuration.TeamA.Members.Count(player =>
            machine.Snapshot().ReadyPlayers.Contains(player));
        var readyB = machine.Configuration.TeamB.Members.Count(player =>
            machine.Snapshot().ReadyPlayers.Contains(player));
        return ValueTask.FromResult(CommandResult.Ok(
            $"[ANO] Tournament {machine.Configuration.MatchId:D}: {machine.State}; "
            + $"map {machine.CurrentMapIndex + 1}/{Math.Max(1, machine.Maps.Count)} {Sanitize(map)}; "
            + $"series {machine.TeamAMaps}-{machine.TeamBMaps}; "
            + $"ready {readyA}/{machine.Configuration.TeamA.Members.Count} vs "
            + $"{readyB}/{machine.Configuration.TeamB.Members.Count}."));
    }

    private ValueTask<CommandResult> MutateAdminAsync(
        CommandContext context,
        string action,
        Func<TournamentMatchStateMachine, MutationResult> mutation)
        => MutateAsync(
            context,
            action,
            expectedSession: null,
            authorize: null,
            mutation);

    private async ValueTask<CommandResult> ReadyAsync(CommandContext context)
    {
        if (!TryConnectedCaller(context.Caller, out var player) || player is null)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A connected tournament player is required.");

        return await MutateAsync(
            context,
            "ready",
            player.SessionId,
            machine => machine.Configuration.TeamFor(player.Id) is not null,
            machine =>
            {
                var allReady = machine.Ready(player.Id);
                return MutationResult.Save(allReady
                    ? "[ANO] Ready recorded; both rosters are ready."
                    : "[ANO] Ready recorded.");
            }).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> CommitMapsAsync(CommandContext context)
    {
        string[] maps;
        try
        {
            maps = context.Get<string>("maps")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Maps must be supplied as one comma-separated series.");
        }

        return await MutateAdminAsync(
            context,
            "maps",
            machine =>
            {
                machine.BeginVeto();
                machine.CompleteVeto(maps);
                return MutationResult.Save(
                    $"[ANO] Tournament maps committed: {string.Join(", ", maps.Select(Sanitize))}.");
            }).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> KnifeAsync(CommandContext context)
    {
        if (!TryTeam(context.Get<string>("team"), out var team))
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput, "Team must be a or b.");

        return await MutateAdminAsync(
            context,
            "knife",
            machine =>
            {
                machine.CompleteKnife(team);
                return MutationResult.Save($"[ANO] Knife winner recorded: {team}.");
            }).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> SideAsync(CommandContext context)
    {
        if (!TryConnectedCaller(context.Caller, out var player) || player is null)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A connected tournament captain is required.");
        if (!TrySide(context.Get<string>("side"), out var side))
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Side must be t or ct.");

        return await MutateAsync(
            context,
            "side",
            player.SessionId,
            machine =>
            {
                var chooser = machine.SideChooser;
                if (chooser is null) return false;
                var captain = chooser == TournamentTeamSlot.TeamA
                    ? machine.Configuration.TeamA.Captain
                    : machine.Configuration.TeamB.Captain;
                return captain == player.Id;
            },
            machine =>
            {
                machine.ChooseSide(machine.SideChooser!.Value, side);
                return MutationResult.Save(
                    $"[ANO] Starting side selected: {side}.");
            }).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> MapWinAsync(CommandContext context)
    {
        if (!TryTeam(context.Get<string>("team"), out var team))
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput, "Team must be a or b.");

        return await MutateAdminAsync(
            context,
            "map-win",
            machine =>
            {
                var completed = machine.CompleteMap(team);
                return completed
                    ? MutationResult.Deactivate(
                        $"[ANO] Tournament completed: {machine.TeamAMaps}-{machine.TeamBMaps}.")
                    : MutationResult.Save(
                        $"[ANO] Map recorded: {machine.TeamAMaps}-{machine.TeamBMaps}.");
            }).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> AbandonAsync(CommandContext context)
    {
        var reason = context.TryGet<string>("reason", out var provided)
            && !string.IsNullOrWhiteSpace(provided)
            ? provided.Trim()
            : "Tournament match abandoned.";

        try
        {
            reason = AdminAuditValidation.NormalizeReason(reason);
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }

        return await MutateAsync(
            context,
            "abandon",
            expectedSession: null,
            authorize: null,
            machine => MutationResult.Deactivate("[ANO] Tournament deactivated.", reason))
            .ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> MutateAsync(
        CommandContext context,
        string action,
        PlayerSessionId? expectedSession,
        Func<TournamentMatchStateMachine, bool>? authorize,
        Func<TournamentMatchStateMachine, MutationResult> mutation)
    {
        await _mutation.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var current = _runtime.CurrentSession;
            if (current is null)
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "No active tournament match.");

            if (expectedSession is not null
                && !IsCurrentSession(context.Caller, expectedSession))
            {
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "The player session changed before the tournament command ran.");
            }

            var candidateMachine = TournamentMatchStateMachine.Restore(
                current.Machine.Configuration,
                current.Machine.Snapshot());
            if (authorize is not null && !authorize(candidateMachine))
                return CommandResult.Fail(
                    CommandFailureReason.Forbidden,
                    "The current tournament state does not authorize this player action.");

            MutationResult result;
            try
            {
                result = mutation(candidateMachine);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException
                or ArgumentOutOfRangeException)
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
            }

            if (expectedSession is not null
                && !IsCurrentSession(context.Caller, expectedSession))
            {
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "The player session changed before the tournament change could be persisted.");
            }

            var candidate = new TournamentRecoverySession(candidateMachine, current.Revision);
            var auditReason = result.AuditReason
                ?? $"match={candidateMachine.Configuration.MatchId:D}; state={candidateMachine.State}";
            try
            {
                auditReason = AdminAuditValidation.NormalizeReason(auditReason);
            }
            catch (ArgumentException exception)
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
            }

            if (!await AuditRequestedAsync(
                    action, context.Caller, auditReason, context.CancellationToken)
                    .ConfigureAwait(false))
            {
                return AuditRequestFailed();
            }

            try
            {
                if (result.Deactivate)
                    await _recovery.DeactivateAsync(candidate, context.CancellationToken)
                        .ConfigureAwait(false);
                else
                    await _recovery.SaveAsync(candidate, context.CancellationToken)
                        .ConfigureAwait(false);
            }
            catch (TournamentConcurrencyException)
            {
                await ReloadActiveAsync(context.CancellationToken).ConfigureAwait(false);
                return CommandResult.Fail(
                    CommandFailureReason.HandlerFailed,
                    "Tournament state changed concurrently; current state was reloaded. Retry the command.");
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CommandResult.Fail(
                    CommandFailureReason.HandlerFailed,
                    "The tournament change was audited but could not be persisted.");
            }

            _runtime.Replace(result.Deactivate ? null : candidate);

            if (!await AuditCompletedAsync(
                    action, context.Caller, auditReason, context.CancellationToken)
                    .ConfigureAwait(false))
            {
                return PersistedButAuditFailed();
            }

            return CommandResult.Ok(result.Message);
        }
        finally
        {
            _mutation.Release();
        }
    }

    private async ValueTask ReloadActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            _runtime.Replace(await _recovery.RestoreActiveAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _runtime.Replace(null);
        }
    }

    private async ValueTask<bool> AuditRequestedAsync(
        string action,
        PlayerId? actor,
        string reason,
        CancellationToken cancellationToken)
        => await AuditAsync(
            new AdminActionId($"tournament.{action}.requested"),
            actor,
            reason,
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<bool> AuditCompletedAsync(
        string action,
        PlayerId? actor,
        string reason,
        CancellationToken cancellationToken)
        => await AuditAsync(
            new AdminActionId($"tournament.{action}"),
            actor,
            reason,
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<bool> AuditAsync(
        AdminActionId action,
        PlayerId? actor,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(
                action,
                actor,
                targetId: null,
                reason,
                _time.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool TryConnectedCaller(PlayerId? caller, out PlayerSnapshot? player)
    {
        if (caller is not null
            && _players.TryGet(caller, out player)
            && player is { IsConnected: true })
        {
            return true;
        }

        player = null;
        return false;
    }

    private bool IsCurrentSession(PlayerId? caller, PlayerSessionId sessionId)
        => caller is not null
            && _players.TryGet(caller, out var current)
            && current is { IsConnected: true }
            && current.SessionId == sessionId;

    private static bool TryTeam(string raw, out TournamentTeamSlot team)
    {
        team = raw.Trim().ToLowerInvariant() switch
        {
            "a" or "teama" or "team-a" => TournamentTeamSlot.TeamA,
            "b" or "teamb" or "team-b" => TournamentTeamSlot.TeamB,
            _ => 0,
        };
        return team is TournamentTeamSlot.TeamA or TournamentTeamSlot.TeamB;
    }

    private static bool TrySide(string raw, out PlayerTeam side)
    {
        side = raw.Trim().ToLowerInvariant() switch
        {
            "t" or "terrorist" => PlayerTeam.Terrorist,
            "ct" or "counterterrorist" or "counter-terrorist"
                => PlayerTeam.CounterTerrorist,
            _ => PlayerTeam.Unknown,
        };
        return side is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character == '|' ? '/' : character));

    private static CommandResult AuditRequestFailed()
        => CommandResult.Fail(
            CommandFailureReason.HandlerFailed,
            "The tournament command could not be audited; no state change was attempted.");

    private static CommandResult PersistedButAuditFailed()
        => CommandResult.Fail(
            CommandFailureReason.HandlerFailed,
            "The tournament state changed successfully, but completion could not be audited.");

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var index = _registrations.Count - 1; index >= 0; index--)
            _registrations[index].Dispose();
        _registrations.Clear();
        _mutation.Dispose();
    }

    private sealed record MutationResult(
        string Message,
        bool Deactivate,
        string? AuditReason)
    {
        public static MutationResult Save(string message, string? auditReason = null)
            => new(message, Deactivate: false, auditReason);

        public static MutationResult Deactivate(string message, string? auditReason = null)
            => new(message, Deactivate: true, auditReason);
    }
}
