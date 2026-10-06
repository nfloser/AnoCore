using AnoCore.Runtime.Management;

namespace AnoCore.Management.Host;

public sealed class ManagementSidecarOptions
{
    public const int HardMaximumRequestBodyBytes = ManagementHttpAdapter.MaxBodyCharacters;

    public string PipeName { get; set; } = "anocore-management";
    public int ConnectTimeoutMilliseconds { get; set; } = 5_000;
    public int ExchangeTimeoutMilliseconds { get; set; } = 30_000;
    public int MaximumRequestBodyBytes { get; set; } = HardMaximumRequestBodyBytes;
    public int RequestsPerMinutePerClient { get; set; } = 240;

    public TimeSpan ConnectTimeout => TimeSpan.FromMilliseconds(ConnectTimeoutMilliseconds);
    public TimeSpan ExchangeTimeout => TimeSpan.FromMilliseconds(ExchangeTimeoutMilliseconds);

    public static IReadOnlyCollection<string> Validate(ManagementSidecarOptions? options)
    {
        if (options is null) return ["Management sidecar configuration is required."];

        var errors = new List<string>();
        if (!ManagementBridgeConfiguration.ValidPipeName(options.PipeName))
            errors.Add("PipeName must contain 1-64 ASCII letters, numbers, dots, underscores or hyphens.");
        if (options.ConnectTimeoutMilliseconds is < 100 or > 60_000)
            errors.Add("ConnectTimeoutMilliseconds must be between 100 and 60000.");
        if (options.ExchangeTimeoutMilliseconds is < 100 or > 60_000)
            errors.Add("ExchangeTimeoutMilliseconds must be between 100 and 60000.");
        if (options.MaximumRequestBodyBytes is < 1 or > HardMaximumRequestBodyBytes)
            errors.Add($"MaximumRequestBodyBytes must be between 1 and {HardMaximumRequestBodyBytes}.");
        if (options.RequestsPerMinutePerClient is < 1 or > 100_000)
            errors.Add("RequestsPerMinutePerClient must be between 1 and 100000.");
        return errors;
    }
}
