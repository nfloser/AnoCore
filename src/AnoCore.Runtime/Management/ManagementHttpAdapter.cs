using System.Text.Json;
using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed record ManagementHttpRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Body = null,
    string? RemoteIdentity = null);

public sealed record ManagementHttpResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

public sealed class ManagementHttpAdapter
{
    public const int MaxBodyCharacters = 16 * 1024;
    public const int MaxHeaders = 32;
    public const int MaxHeaderValueCharacters = 1024;
    public const int MaxPathCharacters = 256;

    private const string TokenHeader = "X-AnoCore-Token";
    private const string CorrelationHeader = "X-Correlation-ID";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ManagementApiGateway _gateway;

    public ManagementHttpAdapter(ManagementApiGateway gateway)
        => _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public async ValueTask<ManagementHttpResponse> HandleAsync(
        ManagementHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryValidateRequest(request, out var validated, out var status))
        {
            return Error(status, validated.CorrelationId, "invalid_request");
        }

        if (!TryRoute(validated.Path, out var version, out var kind, out var resource))
        {
            return Error(404, validated.CorrelationId, "not_found");
        }

        if (!string.Equals(version, ManagementApiVersion.Current, StringComparison.Ordinal))
        {
            return Error(400, validated.CorrelationId, "unsupported_version");
        }

        if (kind == RouteKind.Status)
        {
            if (!string.Equals(validated.Method, "GET", StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(validated.Body)
                || !TryStatusResource(resource, out var statusResource))
            {
                return Error(400, validated.CorrelationId, "invalid_request");
            }

            var response = await _gateway.GetStatusAsync(
                new ManagementRequestEnvelope<ManagementStatusRequest>(
                    version,
                    validated.TokenId,
                    validated.CorrelationId,
                    new ManagementStatusRequest(statusResource)),
                validated.Secret,
                validated.RemoteIdentity,
                cancellationToken).ConfigureAwait(false);

            return Json(MapStatus(response.Success, response.Code), response);
        }

        if (!string.Equals(validated.Method, "POST", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(resource))
        {
            return Error(400, validated.CorrelationId, "invalid_request");
        }

        IReadOnlyDictionary<string, string> arguments;
        try
        {
            arguments = ParseArguments(validated.Body);
        }
        catch (JsonException)
        {
            return Error(400, validated.CorrelationId, "invalid_json");
        }
        catch (ArgumentException)
        {
            return Error(400, validated.CorrelationId, "invalid_request");
        }

        ManagementOperationRequest operation;
        try
        {
            operation = new ManagementOperationRequest(
                new ManagementCapabilityId(resource),
                arguments);
        }
        catch (ArgumentException)
        {
            return Error(400, validated.CorrelationId, "invalid_request");
        }

        var operationResponse = await _gateway.ExecuteAsync(
            new ManagementRequestEnvelope<ManagementOperationRequest>(
                version,
                validated.TokenId,
                validated.CorrelationId,
                operation),
            validated.Secret,
            validated.RemoteIdentity,
            cancellationToken).ConfigureAwait(false);

        return Json(
            MapStatus(operationResponse.Success, operationResponse.Code),
            operationResponse);
    }

    private static bool TryValidateRequest(
        ManagementHttpRequest request,
        out ValidatedRequest validated,
        out int errorStatus)
    {
        validated = new ValidatedRequest(
            string.Empty, string.Empty, string.Empty, "invalid", string.Empty, null, null);
        errorStatus = 400;

        var method = request.Method?.Trim().ToUpperInvariant();
        var path = request.Path?.Trim();
        if (method is not ("GET" or "POST")
            || string.IsNullOrWhiteSpace(path)
            || path.Length > MaxPathCharacters
            || path.Any(char.IsControl)
            || request.Body?.Length > MaxBodyCharacters)
        {
            if (request.Body?.Length > MaxBodyCharacters)
            {
                errorStatus = 413;
            }

            return false;
        }

        var headers = request.Headers ?? new Dictionary<string, string>();
        if (headers.Count > MaxHeaders)
        {
            errorStatus = 431;
            return false;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            if (!ValidHeaderName(pair.Key)
                || pair.Value is null
                || pair.Value.Length > MaxHeaderValueCharacters
                || pair.Value.Any(character => character is '\r' or '\n'))
            {
                errorStatus = pair.Value?.Length > MaxHeaderValueCharacters ? 431 : 400;
                return false;
            }

            if (!normalized.TryAdd(pair.Key, pair.Value.Trim()))
            {
                return false;
            }
        }

        if (!normalized.TryGetValue(CorrelationHeader, out var correlation)
            || !ValidCorrelation(correlation))
        {
            return false;
        }

        correlation = correlation.Trim();
        if (!normalized.TryGetValue(TokenHeader, out var tokenId)
            || !ValidTokenId(tokenId)
            || !normalized.TryGetValue("Authorization", out var authorization)
            || !TryBearerSecret(authorization, out var secret))
        {
            validated = validated with { CorrelationId = correlation };
            errorStatus = 401;
            return false;
        }

        string? remoteIdentity = null;
        if (!string.IsNullOrWhiteSpace(request.RemoteIdentity))
        {
            var remote = request.RemoteIdentity.Trim();
            if (remote.Length > 128 || remote.Any(char.IsControl))
            {
                return false;
            }

            remoteIdentity = remote;
        }

        validated = new ValidatedRequest(
            method,
            path,
            tokenId.Trim(),
            correlation,
            secret,
            request.Body,
            remoteIdentity);
        return true;
    }

    private static bool TryRoute(
        string path,
        out string version,
        out RouteKind kind,
        out string resource)
    {
        version = string.Empty;
        kind = default;
        resource = string.Empty;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !string.Equals(parts[0], "api", StringComparison.Ordinal)
            || !ValidPathSegment(parts[1])
            || !ValidPathSegment(parts[2])
            || !ValidPathSegment(parts[3]))
        {
            return false;
        }

        version = parts[1];
        resource = parts[3];

        if (string.Equals(parts[2], "status", StringComparison.Ordinal))
        {
            kind = RouteKind.Status;
            return true;
        }

        if (string.Equals(parts[2], "operations", StringComparison.Ordinal))
        {
            kind = RouteKind.Operation;
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> ParseArguments(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new Dictionary<string, string>();
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Management operation body must be an object.", nameof(body));
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (result.Count >= 32
                || property.Value.ValueKind != JsonValueKind.String
                || !result.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
            {
                throw new ArgumentException("Management operation arguments are invalid.", nameof(body));
            }
        }

        return result;
    }

    private static bool TryStatusResource(
        string value,
        out ManagementStatusResource resource)
    {
        resource = value switch
        {
            "health" => ManagementStatusResource.Health,
            "server" => ManagementStatusResource.Server,
            "players" => ManagementStatusResource.Players,
            "modules" => ManagementStatusResource.Modules,
            _ => default,
        };

        return resource != default;
    }

    private static bool TryBearerSecret(string authorization, out string secret)
    {
        secret = string.Empty;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = authorization[prefix.Length..].Trim();
        if (value.Length is < 24 or > 512 || value.Any(char.IsControl))
        {
            return false;
        }

        secret = value;
        return true;
    }

    private static bool ValidTokenId(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length <= 64
            && value.Trim().All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool ValidCorrelation(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length <= 64
            && !value.Any(char.IsControl);

    private static bool ValidHeaderName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 64
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool ValidPathSegment(string value)
        => value.Length is >= 1 and <= 64
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static int MapStatus(bool success, string code)
    {
        if (success)
        {
            return 200;
        }

        return code switch
        {
            "unauthorized" => 401,
            "forbidden" => 403,
            "not_found" => 404,
            "rate_limited" => 429,
            "unsupported_version" or "invalid_request" => 400,
            "handler_failed" or "audit_failed" or "audit_failed_after_execution" => 503,
            _ => 400,
        };
    }

    private static ManagementHttpResponse Error(
        int status,
        string correlationId,
        string code)
        => Json(
            status,
            new WireError(
                ManagementApiVersion.Current,
                correlationId,
                false,
                code));

    private static ManagementHttpResponse Json<T>(int status, T payload)
        => new(
            status,
            JsonSerializer.Serialize(payload, JsonOptions),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json; charset=utf-8",
                ["Cache-Control"] = "no-store",
                ["X-Content-Type-Options"] = "nosniff",
            });

    private enum RouteKind
    {
        Status = 1,
        Operation = 2,
    }

    private sealed record ValidatedRequest(
        string Method,
        string Path,
        string TokenId,
        string CorrelationId,
        string Secret,
        string? Body,
        string? RemoteIdentity);

    private sealed record WireError(
        string Version,
        string CorrelationId,
        bool Success,
        string Code);
}
