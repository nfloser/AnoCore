using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed class ManagementApiGateway
{
    private readonly ManagementTokenAuthenticator _authenticator;
    private readonly ManagementCapabilityRegistry _capabilities;
    private readonly IManagementStatusProvider _status;

    public ManagementApiGateway(
        ManagementTokenAuthenticator authenticator,
        ManagementCapabilityRegistry capabilities,
        IManagementStatusProvider status)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public async ValueTask<ManagementResponseEnvelope<ManagementOperationResult>>
        ExecuteAsync(
            ManagementRequestEnvelope<ManagementOperationRequest> request,
            string secret,
            string? remoteIdentity = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authentication = Authenticate(
            request.Version,
            request.TokenId,
            request.CorrelationId,
            secret,
            remoteIdentity);
        if (authentication.Failure is not null)
        {
            return new ManagementResponseEnvelope<ManagementOperationResult>(
                request.CorrelationId,
                false,
                authentication.Failure.Code,
                authentication.Failure);
        }

        var result = await _capabilities.ExecuteAsync(
            authentication.Context!,
            request.Payload,
            cancellationToken).ConfigureAwait(false);
        return new ManagementResponseEnvelope<ManagementOperationResult>(
            request.CorrelationId,
            result.Success,
            result.Code,
            result);
    }

    public async ValueTask<ManagementResponseEnvelope<ManagementStatusPayload>>
        GetStatusAsync(
            ManagementRequestEnvelope<ManagementStatusRequest> request,
            string secret,
            string? remoteIdentity = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authentication = Authenticate(
            request.Version,
            request.TokenId,
            request.CorrelationId,
            secret,
            remoteIdentity);
        if (authentication.Failure is not null)
        {
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                authentication.Failure.Code);
        }

        var context = authentication.Context!;
        if (!context.Principal.Has(ManagementScope.ReadStatus))
        {
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                "forbidden");
        }

        ManagementStatusPayload payload = request.Payload.Resource switch
        {
            ManagementStatusResource.Health => new(
                Health: await _status.GetHealthAsync(cancellationToken).ConfigureAwait(false)),
            ManagementStatusResource.Server => new(
                Server: await _status.GetServerAsync(cancellationToken).ConfigureAwait(false)),
            ManagementStatusResource.Players => new(
                Players: await _status.GetPlayersAsync(cancellationToken).ConfigureAwait(false)),
            ManagementStatusResource.Modules => new(
                Modules: await _status.GetModulesAsync(cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException(
                "Validated management status resource is unsupported."),
        };

        return new ManagementResponseEnvelope<ManagementStatusPayload>(
            request.CorrelationId,
            true,
            "ok",
            payload);
    }

    private AuthenticationResult Authenticate(
        string version,
        string tokenId,
        string correlationId,
        string secret,
        string? remoteIdentity)
    {
        if (!string.Equals(version, ManagementApiVersion.Current, StringComparison.Ordinal))
        {
            return AuthenticationResult.Fail(
                ManagementOperationResult.Fail(
                    "unsupported_version",
                    $"Management API version '{version}' is not supported."));
        }

        var principal = _authenticator.Authenticate(tokenId, secret);
        if (principal is null)
        {
            return AuthenticationResult.Fail(
                ManagementOperationResult.Fail(
                    "unauthorized",
                    "Management credentials were rejected."));
        }

        return AuthenticationResult.Success(
            new ManagementRequestContext(
                principal,
                correlationId,
                remoteIdentity));
    }

    private sealed record AuthenticationResult(
        ManagementRequestContext? Context,
        ManagementOperationResult? Failure)
    {
        public static AuthenticationResult Success(ManagementRequestContext context)
            => new(context, null);

        public static AuthenticationResult Fail(ManagementOperationResult failure)
            => new(null, failure);
    }
}
