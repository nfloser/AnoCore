using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed class ManagementApiGateway
{
    private readonly ManagementTokenAuthenticator _authenticator;
    private readonly ManagementCapabilityRegistry _capabilities;
    private readonly IManagementStatusProvider _status;
    private readonly ManagementRateLimiter _rateLimiter;
    private readonly TimeProvider _time;
    private readonly Func<ManagementAuditEvent, CancellationToken, ValueTask>? _audit;

    public ManagementApiGateway(
        ManagementTokenAuthenticator authenticator,
        ManagementCapabilityRegistry capabilities,
        IManagementStatusProvider status,
        ManagementRateLimiter? rateLimiter = null,
        TimeProvider? timeProvider = null,
        Func<ManagementAuditEvent, CancellationToken, ValueTask>? audit = null)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _rateLimiter = rateLimiter ?? new ManagementRateLimiter();
        _time = timeProvider ?? TimeProvider.System;
        _audit = audit;
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

        var now = _time.GetUtcNow();
        if (!_rateLimiter.TryAcquire(
                context.Principal,
                ManagementOperationClass.Read,
                now,
                out _))
        {
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                "rate_limited");
        }

        var capability = StatusCapability(request.Payload.Resource);
        if (!await AuditStatusAsync(
                "requested",
                context,
                capability,
                null,
                now,
                cancellationToken).ConfigureAwait(false))
        {
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                "audit_failed");
        }

        ManagementStatusPayload payload;
        try
        {
            payload = request.Payload.Resource switch
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _ = await AuditStatusAsync(
                "failed",
                context,
                capability,
                "handler_failed",
                _time.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                "handler_failed");
        }

        if (!await AuditStatusAsync(
                "completed",
                context,
                capability,
                "ok",
                _time.GetUtcNow(),
                cancellationToken).ConfigureAwait(false))
        {
            return new ManagementResponseEnvelope<ManagementStatusPayload>(
                request.CorrelationId,
                false,
                "audit_failed_after_execution");
        }

        return new ManagementResponseEnvelope<ManagementStatusPayload>(
            request.CorrelationId,
            true,
            "ok",
            payload);
    }

    private async ValueTask<bool> AuditStatusAsync(
        string phase,
        ManagementRequestContext context,
        ManagementCapabilityId capability,
        string? resultCode,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (_audit is null) return true;
        try
        {
            await _audit(
                new ManagementAuditEvent(
                    phase,
                    context.Principal.TokenId,
                    capability,
                    context.CorrelationId,
                    resultCode,
                    occurredAtUtc),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static ManagementCapabilityId StatusCapability(
        ManagementStatusResource resource)
        => new(resource switch
        {
            ManagementStatusResource.Health => "status.health",
            ManagementStatusResource.Server => "status.server",
            ManagementStatusResource.Players => "status.players",
            ManagementStatusResource.Modules => "status.modules",
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        });

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
