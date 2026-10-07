using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Permissions;

namespace AnoCore.Modules.Admin;

public sealed class RoleAdministrationCommands : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    public RoleAdministrationCommands(IAnoCommandRegistry commands, MySqlRoleAdministration service, IAuthorizationStore store)
    {
        var owner = new ModuleId("ano.admin.roles");
        try
        {
            foreach (var revoke in new[] { false, true })
            {
                var name = revoke ? "anorevokerole" : "anograntrole";
                _owned.Add(commands.Register(owner, new(name, "Change a durable role assignment.",
                    new("ano.admin.roles." + (revoke ? "revoke" : "grant")), arguments: [
                        new("target", CommandArgumentKind.String, "Explicit SteamID64."),
                        new("role", CommandArgumentKind.String, "Defined role ID."),
                        new("minutes", CommandArgumentKind.Int32, "0 permanent, otherwise bounded minutes."),
                        new("reason", CommandArgumentKind.String, "Audit reason.", required: false)]), async context =>
                {
                    if (!ulong.TryParse(context.Get<string>("target"), out var id))
                        return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use an explicit SteamID64.");
                    try
                    {
                        await service.ChangeAsync(context.Caller, new PlayerId(id), new RoleId(context.Get<string>("role")),
                            context.Get<int>("minutes"), revoke, context.TryGet<string>("reason", out var reason) ? reason ?? "" : "", context.CancellationToken);
                        return CommandResult.Ok("[ANO] Role assignment committed and authorization refreshed.");
                    }
                    catch (UnauthorizedAccessException) { return CommandResult.Fail(CommandFailureReason.Forbidden, "Role delegation denied."); }
                    catch (ArgumentException) { return CommandResult.Fail(CommandFailureReason.InvalidInput, "Invalid role, SteamID or duration."); }
                }));
            }
            _owned.Add(commands.Register(owner, new("anoroledefine", "Define a role from the server console.", new("ano.admin.roles.define"),
                arguments: [new("role", CommandArgumentKind.String, "Role ID."), new("immunity", CommandArgumentKind.Int32, "Immunity."),
                    new("permissions", CommandArgumentKind.String, "Comma-separated allow rules or - for none."),
                    new("tags", CommandArgumentKind.String, "Comma-separated tags or -.", required: false),
                    new("parents", CommandArgumentKind.String, "Comma-separated parent IDs or -.", required: false)]), async context =>
            {
                if (context.Caller is not null) return CommandResult.Fail(CommandFailureReason.Forbidden, "Server console required.");
                try
                {
                    var permissions = context.Get<string>("permissions");
                    var tags = context.TryGet<string>("tags", out var text) ? text ?? "-" : "-";
                    var parents = context.TryGet<string>("parents", out var parentText) ? parentText ?? "-" : "-";
                    await service.DefineAsync(null, new(new(context.Get<string>("role")), context.Get<int>("immunity"),
                        parents == "-" ? [] : parents.Split(',').Select(value => new RoleId(value)).ToArray(),
                        permissions == "-" ? [] : permissions.Split(',').Select(value => new PermissionRule(value.StartsWith("deny:", StringComparison.Ordinal) ? value[5..] : value,
                            value.StartsWith("deny:", StringComparison.Ordinal) ? PermissionEffect.Deny : PermissionEffect.Allow)).ToArray(),
                        tags == "-" ? [] : tags.Split(',')), context.CancellationToken);
                    return CommandResult.Ok("[ANO] Role definition committed.");
                }
                catch (ArgumentException) { return CommandResult.Fail(CommandFailureReason.InvalidInput, "Invalid role definition."); }
            }));
            _owned.Add(commands.Register(owner, new("anoroles", "Inspect role definitions and assignments.", new("ano.admin.roles.inspect"),
                arguments: [new("target", CommandArgumentKind.String, "Optional explicit SteamID64.", required: false)]), async context =>
            {
                var state = await store.LoadAsync(context.CancellationToken) ?? AuthorizationState.Empty;
                if (!context.TryGet<string>("target", out var target))
                    return CommandResult.Ok("[ANO] Roles: " + string.Join(", ", state.Roles.Take(64).Select(role => role.Id.Value)));
                if (!ulong.TryParse(target, out var id)) return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use SteamID64.");
                var player = state.Players.FirstOrDefault(value => value.PlayerId.SteamId64 == id);
                return CommandResult.Ok("[ANO] Roles: " + (player is null ? "none" : string.Join(", ", player.Roles.Select(role => role.Value)
                    .Concat(player.TimedRoles.Select(grant => $"{grant.Role.Value} until {grant.ExpiresAtUtc:O}")))));
            }));
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        foreach (var handle in _owned.AsEnumerable().Reverse()) handle.Dispose();
        _owned.Clear();
    }
}
