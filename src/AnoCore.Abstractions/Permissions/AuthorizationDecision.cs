namespace AnoCore.Abstractions.Permissions;

public enum AuthorizationDecisionSource
{
    None = 0,
    Direct = 1,
    Role = 2,
}

public sealed record AuthorizationDecision(
    bool IsAllowed,
    PermissionEffect? Effect,
    AuthorizationDecisionSource Source,
    PermissionRule? Rule,
    RoleId? Role)
{
    public static AuthorizationDecision DeniedByDefault { get; } = new(
        false,
        null,
        AuthorizationDecisionSource.None,
        null,
        null);
}
