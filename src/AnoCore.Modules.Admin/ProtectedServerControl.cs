using System.Text;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ProtectedServerControlConfiguration
{
    public string[] AllowedConVars { get; init; } = [];

    public string[] AllowedServerCommands { get; init; } = [];
}

public sealed class ProtectedServerControlPolicy
{
    private static readonly Regex SafeName = new(
        "^[a-z][a-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> DeniedConVars =
        new(StringComparer.Ordinal)
        {
            "rcon_password",
            "sv_password",
            "sv_setsteamaccount",
        };

    private static readonly HashSet<string> DeniedServerCommands =
        new(StringComparer.Ordinal)
        {
            "quit",
            "exit",
            "exec",
            "map",
            "changelevel",
            "host_workshop_map",
            "plugin_load",
            "plugin_unload",
            "rcon",
            "meta",
        };

    private readonly HashSet<string> _allowedConVars;
    private readonly HashSet<string> _allowedServerCommands;

    private ProtectedServerControlPolicy(
        IEnumerable<string> allowedConVars,
        IEnumerable<string> allowedServerCommands)
    {
        _allowedConVars = new HashSet<string>(
            allowedConVars,
            StringComparer.Ordinal);
        _allowedServerCommands = new HashSet<string>(
            allowedServerCommands,
            StringComparer.Ordinal);
    }

    public static ProtectedServerControlPolicy Create(
        ProtectedServerControlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = Validate(configuration);
        if (errors.Count != 0)
        {
            throw new ArgumentException(
                string.Join(" ", errors),
                nameof(configuration));
        }

        return new ProtectedServerControlPolicy(
            Normalize(configuration.AllowedConVars ?? [])
                .Where(name => !IsDeniedConVar(name)),
            Normalize(configuration.AllowedServerCommands ?? [])
                .Where(name => !DeniedServerCommands.Contains(name)));
    }

    public static IReadOnlyCollection<string> Validate(
        ProtectedServerControlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        List<string> errors = [];

        ValidateCollection(
            configuration.AllowedConVars ?? [],
            "AllowedConVars",
            errors);
        ValidateCollection(
            configuration.AllowedServerCommands ?? [],
            "AllowedServerCommands",
            errors);

        if ((configuration.AllowedConVars?.Length ?? 0) > 128)
        {
            errors.Add("AllowedConVars cannot contain more than 128 entries.");
        }

        if ((configuration.AllowedServerCommands?.Length ?? 0) > 64)
        {
            errors.Add("AllowedServerCommands cannot contain more than 64 entries.");
        }

        return errors;
    }

    public bool AllowsConVar(string name)
        => TryNormalizeName(name, out var normalized)
           && !IsDeniedConVar(normalized)
           && _allowedConVars.Contains(normalized);

    public bool AllowsServerCommand(string name)
        => TryNormalizeName(name, out var normalized)
           && !DeniedServerCommands.Contains(normalized)
           && _allowedServerCommands.Contains(normalized);

    public static bool TryNormalizeName(
        string value,
        out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return SafeName.IsMatch(normalized);
    }

    private static bool IsDeniedConVar(string name)
        => DeniedConVars.Contains(name)
           || name.Contains("password", StringComparison.Ordinal)
           || name.Contains("passwd", StringComparison.Ordinal)
           || name.Contains("token", StringComparison.Ordinal)
           || name.Contains("secret", StringComparison.Ordinal)
           || name.Contains("steamaccount", StringComparison.Ordinal)
           || name.EndsWith("_key", StringComparison.Ordinal)
           || name.StartsWith("key_", StringComparison.Ordinal);

    private static IEnumerable<string> Normalize(
        IEnumerable<string> values)
        => values
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value.Length != 0)
            .Distinct(StringComparer.Ordinal);

    private static void ValidateCollection(
        IEnumerable<string> values,
        string label,
        List<string> errors)
    {
        foreach (var value in values)
        {
            if (!TryNormalizeName(value, out _))
            {
                errors.Add(
                    $"{label} contains an invalid name. Names must start with a letter and contain only letters, numbers, '.', '_' or '-'.");
                return;
            }
        }
    }
}

public sealed record SameIpPlayer(
    PlayerId Id,
    string Name);

public sealed record SameIpPlayerGroup(
    string NetworkFingerprint,
    IReadOnlyList<SameIpPlayer> Players);

public interface IProtectedServerControlTransport
{
    ValueTask<bool> SetCVarAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default);

    ValueTask ExecuteServerCommandAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SameIpPlayerGroup>> GetSameIpGroupsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ProtectedServerControlExecutor
{
    public const int MaxOutputLength = 4096;
    public const int MaxValueLength = 256;
    public const int MaxGroups = 20;
    public const int MaxPlayersPerGroup = 8;

    private static readonly PermissionId CVarPermission =
        new("ano.admin.cvar");
    private static readonly PermissionId ServerCommandPermission =
        new("ano.admin.server");
    private static readonly PermissionId SameIpPermission =
        new("ano.admin.sameip");

    private static readonly AdminActionId CVarAuditAction =
        new("extended.cvar.attempt");
    private static readonly AdminActionId ServerCommandAuditAction =
        new("extended.server-command.attempt");
    private static readonly AdminActionId SameIpAuditAction =
        new("extended.same-ip.inspect");

    private readonly ProtectedServerControlPolicy _policy;
    private readonly IPermissionEvaluator _permissions;
    private readonly IAdminAuditService _audit;
    private readonly IProtectedServerControlTransport _transport;
    private readonly TimeProvider _timeProvider;

    public ProtectedServerControlExecutor(
        ProtectedServerControlPolicy policy,
        IPermissionEvaluator permissions,
        IAdminAuditService audit,
        IProtectedServerControlTransport transport,
        TimeProvider? timeProvider = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CommandResult> SetCVarAsync(
        PlayerId? actor,
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (!ProtectedServerControlPolicy.TryNormalizeName(
                name,
                out var normalized)
            || !_policy.AllowsConVar(normalized))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "That ConVar is not permitted by the protected server-control policy.");
        }

        if (!IsSafeArgument(value, allowEmpty: false))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "The ConVar value contains unsupported or unsafe characters.");
        }

        if (!await HasPermissionAsync(
                actor,
                CVarPermission,
                cancellationToken).ConfigureAwait(false))
        {
            return CommandResult.Fail(
                CommandFailureReason.Forbidden,
                "You are not allowed to change protected server ConVars.");
        }

        try
        {
            await _audit.RecordAsync(
                CVarAuditAction,
                actor,
                null,
                $"set cvar {normalized} value=<redacted>",
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            var changed = await _transport.SetCVarAsync(
                normalized,
                value.Trim(),
                cancellationToken).ConfigureAwait(false);

            return changed
                ? CommandResult.Ok($"Updated protected ConVar '{normalized}'.")
                : CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "The configured ConVar does not exist on this server.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The protected ConVar operation failed.");
        }
    }

    public async ValueTask<CommandResult> ExecuteServerCommandAsync(
        PlayerId? actor,
        string command,
        string? arguments,
        CancellationToken cancellationToken = default)
    {
        if (!ProtectedServerControlPolicy.TryNormalizeName(
                command,
                out var normalized)
            || !_policy.AllowsServerCommand(normalized))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "That server command is not permitted by the protected server-control policy.");
        }

        var normalizedArguments = arguments?.Trim() ?? string.Empty;
        if (!IsSafeArgument(normalizedArguments, allowEmpty: true))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "The server-command arguments contain unsupported or unsafe characters.");
        }

        if (!await HasPermissionAsync(
                actor,
                ServerCommandPermission,
                cancellationToken).ConfigureAwait(false))
        {
            return CommandResult.Fail(
                CommandFailureReason.Forbidden,
                "You are not allowed to execute protected server commands.");
        }

        try
        {
            await _audit.RecordAsync(
                ServerCommandAuditAction,
                actor,
                null,
                normalizedArguments.Length == 0
                    ? $"execute server command {normalized}"
                    : $"execute server command {normalized} arguments=<redacted>",
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            await _transport.ExecuteServerCommandAsync(
                normalized,
                normalizedArguments,
                cancellationToken).ConfigureAwait(false);

            return CommandResult.Ok(
                $"Executed protected server command '{normalized}'.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The protected server command failed.");
        }
    }

    public async ValueTask<CommandResult> ListSameIpAsync(
        PlayerId? actor,
        CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(
                actor,
                SameIpPermission,
                cancellationToken).ConfigureAwait(false))
        {
            return CommandResult.Fail(
                CommandFailureReason.Forbidden,
                "You are not allowed to inspect same-network groups.");
        }

        try
        {
            await _audit.RecordAsync(
                SameIpAuditAction,
                actor,
                null,
                "inspect same-network player groups; raw addresses withheld",
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            var groups = await _transport.GetSameIpGroupsAsync(
                cancellationToken).ConfigureAwait(false);

            var builder = new StringBuilder("Same-network groups:");
            var emitted = 0;
            foreach (var group in groups.Take(MaxGroups))
            {
                if (group.Players.Count < 2)
                {
                    continue;
                }

                var fingerprint = SanitizeLabel(
                    group.NetworkFingerprint,
                    48);
                builder.Append(" | ");
                builder.Append(fingerprint);
                builder.Append(": ");

                var players = group.Players
                    .Take(MaxPlayersPerGroup)
                    .Select(player =>
                        $"{SanitizeLabel(player.Name, 64)} ({player.Id})");
                builder.Append(string.Join(", ", players));
                emitted++;

                if (builder.Length >= MaxOutputLength)
                {
                    break;
                }
            }

            if (emitted == 0)
            {
                return CommandResult.Ok(
                    "No connected players share the same normalized network address.");
            }

            var output = builder.ToString();
            if (output.Length > MaxOutputLength)
            {
                output = output[..MaxOutputLength];
            }

            return CommandResult.Ok(output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "Same-network inspection failed.");
        }
    }

    public static PermissionId GetCVarPermission() => CVarPermission;

    public static PermissionId GetServerCommandPermission()
        => ServerCommandPermission;

    public static PermissionId GetSameIpPermission() => SameIpPermission;

    private async ValueTask<bool> HasPermissionAsync(
        PlayerId? actor,
        PermissionId permission,
        CancellationToken cancellationToken)
        => actor is null
           || await _permissions.HasPermissionAsync(
                   actor,
                   permission,
                   cancellationToken).ConfigureAwait(false);

    private static bool IsSafeArgument(
        string value,
        bool allowEmpty)
    {
        if (value.Length > MaxValueLength
            || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character)
                || character == ';'
                || character == (char)96)
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeLabel(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sanitized = new string(
            value.Trim()
                .Where(character => !char.IsControl(character))
                .Take(maxLength)
                .ToArray());

        return sanitized.Length == 0 ? "unknown" : sanitized;
    }
}
