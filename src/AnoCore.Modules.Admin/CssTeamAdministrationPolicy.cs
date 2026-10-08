using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public interface ITeamAdministrationCommandHandler : IDisposable
{
    ValueTask<CommandResult> ExecuteAsync(ExtendedInventoryTeamOperation operation, PlayerId? actor,
        string selector, string? requestedTeam, CancellationToken cancellationToken = default);
}

public static class CssTeamAdministrationPolicy
{
    public const string RequiredFlag = "@anocore/team";

    public static CommandResult? Authorize(bool hasFlag) => hasFlag ? null
        : CommandResult.Fail(CommandFailureReason.Forbidden, "You need the CSS @anocore/team flag to manage player teams.");

    public static CommandResult? CheckTarget(int matches, bool canTarget) => matches switch
    {
        0 => CommandResult.Fail(CommandFailureReason.InvalidInput, "No connected human player matches this target."),
        > 1 => CommandResult.Fail(CommandFailureReason.InvalidInput, "Target is ambiguous; select exactly one player."),
        1 when canTarget => null,
        _ => CommandResult.Fail(CommandFailureReason.Forbidden, "CSS immunity prevents targeting this player."),
    };

    public static bool TryDestination(ExtendedInventoryTeamOperation operation, string? requestedTeam,
        PlayerTeam currentTeam, out PlayerTeam team)
    {
        team = operation switch
        {
            ExtendedInventoryTeamOperation.SetTeam => requestedTeam?.Trim().ToLowerInvariant() switch
            {
                "t" or "terrorist" => PlayerTeam.Terrorist,
                "ct" or "counterterrorist" => PlayerTeam.CounterTerrorist,
                "spec" or "spectator" => PlayerTeam.Spectator,
                _ => PlayerTeam.Unknown,
            },
            ExtendedInventoryTeamOperation.SwapTeam => currentTeam switch
            {
                PlayerTeam.Terrorist => PlayerTeam.CounterTerrorist,
                PlayerTeam.CounterTerrorist => PlayerTeam.Terrorist,
                _ => PlayerTeam.Unknown,
            },
            _ => PlayerTeam.Unknown,
        };
        return team != PlayerTeam.Unknown;
    }
}
