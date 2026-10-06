using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed class ManagementBridgeConfiguration
{
    private static readonly ManagementScope[] SupportedScopes =
    [
        ManagementScope.ReadStatus,
        ManagementScope.ManageServer,
        ManagementScope.ManagePlayers,
        ManagementScope.ManageModules,
    ];

    public bool Enabled { get; set; }

    public string PipeName { get; set; } = "anocore-management";

    public int ReadRequestsPerMinute { get; set; } = 120;

    public int PrivilegedRequestsPerMinute { get; set; } = 30;

    public int MaximumTrackedKeys { get; set; } = 1024;

    public List<ManagementCredentialConfiguration> Credentials { get; set; } = [];

    public static IReadOnlyCollection<string> Validate(
        ManagementBridgeConfiguration? configuration)
    {
        var errors = new List<string>();
        if (configuration is null)
        {
            return ["Management bridge configuration is required."];
        }

        if (!ValidPipeName(configuration.PipeName))
        {
            errors.Add(
                "PipeName must contain 1-64 ASCII letters, numbers, dots, underscores or hyphens.");
        }

        try
        {
            _ = configuration.RateLimits();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            errors.Add(exception.Message);
        }

        if (configuration.Credentials is null)
        {
            errors.Add("Credentials must be an array.");
            return errors;
        }

        if (configuration.Credentials.Count > 128)
        {
            errors.Add("At most 128 management credentials are supported.");
        }

        if (configuration.Enabled && configuration.Credentials.Count == 0)
        {
            errors.Add("At least one management credential is required when the bridge is enabled.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credential in configuration.Credentials)
        {
            if (credential is null)
            {
                errors.Add("Management credentials cannot contain null entries.");
                continue;
            }

            try
            {
                var value = credential.ToCredential();
                if (!ids.Add(value.TokenId))
                {
                    errors.Add($"Duplicate management token id '{value.TokenId}'.");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or ArgumentOutOfRangeException
                or FormatException)
            {
                errors.Add($"Credential '{credential.TokenId}': {exception.Message}");
            }
        }

        return errors;
    }

    public ManagementRateLimitOptions RateLimits()
        => new ManagementRateLimitOptions(
            ReadRequestsPerMinute,
            PrivilegedRequestsPerMinute,
            MaximumTrackedKeys).Validate();

    public IReadOnlyList<ManagementTokenCredential> BuildCredentials()
    {
        var errors = Validate(this);
        if (errors.Count != 0)
        {
            throw new ArgumentException(
                string.Join(" ", errors),
                nameof(ManagementBridgeConfiguration));
        }

        return Credentials.Select(value => value.ToCredential()).ToArray();
    }

    public static bool ValidPipeName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length <= 64
            && value.Trim().All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');

    public static IReadOnlyList<string> ScopeNames(ManagementScope scopes)
        => SupportedScopes
            .Where(scope => (scopes & scope) == scope)
            .Select(scope => scope.ToString())
            .ToArray();
}

public sealed class ManagementCredentialConfiguration
{
    public string TokenId { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public string SaltBase64 { get; set; } = string.Empty;

    public string HashBase64 { get; set; } = string.Empty;

    public int Iterations { get; set; } = ManagementTokenHasher.DefaultIterations;

    public ManagementTokenCredential ToCredential()
        => new(
            TokenId,
            ParseScopes(Scopes),
            SaltBase64,
            HashBase64,
            Iterations);

    public static ManagementCredentialConfiguration FromCredential(
        ManagementTokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new ManagementCredentialConfiguration
        {
            TokenId = credential.TokenId,
            Scopes = ManagementBridgeConfiguration.ScopeNames(credential.Scopes).ToList(),
            SaltBase64 = credential.SaltBase64,
            HashBase64 = credential.HashBase64,
            Iterations = credential.Iterations,
        };
    }

    private static ManagementScope ParseScopes(IReadOnlyCollection<string>? names)
    {
        if (names is null || names.Count == 0 || names.Count > 4)
        {
            throw new ArgumentException("At least one supported management scope is required.");
        }

        var scopes = ManagementScope.None;
        var unique = new HashSet<ManagementScope>();
        foreach (var name in names)
        {
            if (!Enum.TryParse<ManagementScope>(name?.Trim(), ignoreCase: false, out var scope)
                || scope == ManagementScope.None
                || (scope & ~ManagementPrincipal.AllScopes) != 0
                || !unique.Add(scope)
                || !IsSingleScope(scope))
            {
                throw new ArgumentException($"Unsupported or duplicate management scope '{name}'.");
            }

            scopes |= scope;
        }

        return scopes;
    }

    private static bool IsSingleScope(ManagementScope scope)
        => scope is ManagementScope.ReadStatus
            or ManagementScope.ManageServer
            or ManagementScope.ManagePlayers
            or ManagementScope.ManageModules;
}
