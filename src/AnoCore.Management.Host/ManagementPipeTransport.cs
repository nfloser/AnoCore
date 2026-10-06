using AnoCore.Runtime.Management;

namespace AnoCore.Management.Host;

public interface IManagementPipeTransport
{
    ValueTask<ManagementHttpResponse> SendAsync(
        ManagementHttpRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class NamedPipeManagementTransport : IManagementPipeTransport
{
    private readonly ManagementPipeClient _client;

    public NamedPipeManagementTransport(ManagementSidecarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = ManagementSidecarOptions.Validate(options);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));

        _client = new ManagementPipeClient(
            options.PipeName,
            options.ConnectTimeout,
            options.ExchangeTimeout);
    }

    public ValueTask<ManagementHttpResponse> SendAsync(
        ManagementHttpRequest request,
        CancellationToken cancellationToken = default)
        => _client.SendAsync(request, cancellationToken);
}
