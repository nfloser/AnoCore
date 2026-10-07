namespace AnoCore.Abstractions.Permissions;

public sealed record TimedRoleAssignment
{
    public TimedRoleAssignment(RoleId role, DateTimeOffset expiresAtUtc)
    {
        Role = role ?? throw new ArgumentNullException(nameof(role));
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }
    public RoleId Role { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
}
