using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Management;

public static class ManagementApiVersion
{
    public const string Current = "v1";
}

[Flags]
public enum ManagementScope
{
    None = 0,
    ReadStatus = 1 << 0,
    ManageServer = 1 << 1,
    ManagePlayers = 1 << 2,
    ManageModules = 1 << 3,
}

public enum ManagementOperationClass
{
    Read = 1,
    Privileged = 2,
}

public sealed record ManagementPrincipal
{
    public ManagementPrincipal(string tokenId, ManagementScope scopes)
    {
        TokenId = ManagementValidation.Identifier(tokenId, nameof(tokenId), 64);
        if (scopes == ManagementScope.None)
            throw new ArgumentOutOfRangeException(nameof(scopes));
        if ((scopes & ~AllScopes) != 0)
            throw new ArgumentOutOfRangeException(nameof(scopes));
        Scopes = scopes;
    }

    public const ManagementScope AllScopes =
        ManagementScope.ReadStatus
        | ManagementScope.ManageServer
        | ManagementScope.ManagePlayers
        | ManagementScope.ManageModules;

    public string TokenId { get; }
    public ManagementScope Scopes { get; }

    public bool Has(ManagementScope required)
        => required != ManagementScope.None && (Scopes & required) == required;
}

public sealed record ManagementRequestContext
{
    public ManagementRequestContext(
        ManagementPrincipal principal,
        string correlationId,
        string? remoteIdentity = null)
    {
        Principal = principal ?? throw new ArgumentNullException(nameof(principal));
        CorrelationId = ManagementValidation.Text(
            correlationId, nameof(correlationId), 1, 64);
        RemoteIdentity = string.IsNullOrWhiteSpace(remoteIdentity)
            ? null
            : ManagementValidation.Text(
                remoteIdentity, nameof(remoteIdentity), 1, 128);
    }

    public ManagementPrincipal Principal { get; }
    public string CorrelationId { get; }
    public string? RemoteIdentity { get; }
}

public readonly record struct ManagementCapabilityId
{
    public ManagementCapabilityId(string value)
        => Value = ManagementValidation.Identifier(
            value, nameof(value), 64, allowDot: true);

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ManagementOperationRequest
{
    public ManagementOperationRequest(
        ManagementCapabilityId capability,
        IReadOnlyDictionary<string, string>? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(capability.Value))
            throw new ArgumentException(
                "A management capability id is required.", nameof(capability));
        Capability = capability;
        var values = arguments ?? new Dictionary<string, string>();
        if (values.Count > 32)
            throw new ArgumentException(
                "Management operations support at most 32 arguments.", nameof(arguments));

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            var key = ManagementValidation.Identifier(
                pair.Key, nameof(arguments), 64, allowDot: true);
            var value = ManagementValidation.Text(
                pair.Value, nameof(arguments), 0, 1024, allowEmpty: true);
            if (!normalized.TryAdd(key, value))
                throw new ArgumentException(
                    "Management operation argument keys must be unique.", nameof(arguments));
        }

        Arguments = normalized;
    }

    public ManagementCapabilityId Capability { get; }
    public IReadOnlyDictionary<string, string> Arguments { get; }
}

public sealed record ManagementOperationResult(
    bool Success,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public static ManagementOperationResult Ok(
        string message,
        IReadOnlyDictionary<string, string>? data = null)
        => new(true, "ok", message, data);

    public static ManagementOperationResult Fail(string code, string message)
        => new(false,
            ManagementValidation.Identifier(code, nameof(code), 64, allowDot: true),
            ManagementValidation.Text(message, nameof(message), 0, 512, allowEmpty: true));
}

public sealed record ManagementHealthSnapshot(
    bool Ready,
    string RuntimeStatus,
    DateTimeOffset ObservedAtUtc);

public sealed record ManagementServerStatus(
    string ApiVersion,
    int ModuleApiLevel,
    int ConnectedPlayers,
    DateTimeOffset ObservedAtUtc);

public sealed record ManagementPlayerStatus(
    PlayerId PlayerId,
    string DisplayName,
    bool Connected,
    string Team);

public sealed record ManagementModuleStatus(
    ModuleId ModuleId,
    string State);

public interface IManagementStatusProvider
{
    ValueTask<ManagementHealthSnapshot> GetHealthAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ManagementServerStatus> GetServerAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ManagementCapabilityDescriptor(
    ManagementCapabilityId Id,
    string Description,
    ManagementScope RequiredScope,
    ManagementOperationClass OperationClass)
{
    public ManagementCapabilityDescriptor Validate()
    {
        _ = ManagementValidation.Text(
            Description, nameof(Description), 1, 256);
        if (RequiredScope == ManagementScope.None
            || (RequiredScope & ~ManagementPrincipal.AllScopes) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredScope));
        }
        if (!Enum.IsDefined(OperationClass))
            throw new ArgumentOutOfRangeException(nameof(OperationClass));
        return this;
    }
}

public interface IManagementCapabilityRegistry
{
    IDisposable Register(
        ModuleId owner,
        ManagementCapabilityDescriptor descriptor,
        Func<ManagementRequestContext, ManagementOperationRequest,
            CancellationToken, ValueTask<ManagementOperationResult>> handler);

    ValueTask<ManagementOperationResult> ExecuteAsync(
        ManagementRequestContext context,
        ManagementOperationRequest request,
        CancellationToken cancellationToken = default);

    IReadOnlyList<ManagementCapabilityDescriptor> GetCapabilities();
}

internal static class ManagementValidation
{
    public static string Identifier(
        string? value,
        string parameterName,
        int maximum,
        bool allowDot = false)
    {
        var normalized = Text(value, parameterName, 1, maximum).Trim();
        if (normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_'
                    || (allowDot && character == '.'))))
        {
            throw new ArgumentException(
                "Identifier contains unsupported characters.", parameterName);
        }

        return normalized;
    }

    public static string Text(
        string? value,
        string parameterName,
        int minimum,
        int maximum,
        bool allowEmpty = false)
    {
        if (value is null)
            throw new ArgumentNullException(parameterName);
        var normalized = value.Trim();
        if (!allowEmpty && normalized.Length < minimum)
            throw new ArgumentException("Value is too short.", parameterName);
        if (normalized.Length > maximum)
            throw new ArgumentException("Value is too long.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Control characters are not allowed.", parameterName);
        return normalized;
    }
}
